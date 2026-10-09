// ==========================================================================
//  Módulo: Sistema / Administración (usuarios, rutas, historial, respaldos)
//  Pestaña: admin (solo rol Admin).
// ==========================================================================

// --- Copias de seguridad (Administración) ---
async function loadBackups() {
  try {
    const b = await window.api.getBackups();
    const last = b.lastRun
      ? ` Último respaldo: ${formatDate(b.lastRun.at)} (${b.lastRun.success ? 'correcto' : 'con error'}).`
      : '';
    document.getElementById('backup-summary').textContent =
      `Automático todos los días a las ${String(b.hour).padStart(2, '0')}:00; se conservan las últimas ${b.keep} copias de cada base. ` +
      `Carpeta: ${b.directory}.${last}`;
    const tbody = document.getElementById('tbody-backups');
    if (!b.files.length) {
      tbody.innerHTML = '<tr><td class="ta-center c-dim" colspan="3">Todavía no hay copias.</td></tr>';
      return;
    }
    tbody.innerHTML = b.files.map(f => `
      <tr>
        <td>${escapeHtml(f.name)}</td>
        <td>${escapeHtml(formatDate(f.createdAt))}</td>
        <td class="num">${(f.sizeBytes / 1048576).toLocaleString('es-CO', { maximumFractionDigits: 1 })} MB</td>
      </tr>`).join('');
  } catch (err) {
    showToast(err.message, 'error');
  }
}

async function onRunBackup() {
  const btn = document.getElementById('btn-admin-backup');
  btn.disabled = true;
  btn.innerText = 'Respaldando…';
  try {
    const run = await window.api.runBackup();
    showToast(run.message || 'Respaldo completado.');
    loadBackups();
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.innerText = 'Respaldar ahora';
  }
}

async function onConfirmUserDelete() {
  const pending = state.userPendingDelete;
  if (!pending) return;

  const btn = document.getElementById('btn-confirm-user-delete');
  btn.disabled = true;
  btn.innerText = 'Eliminando...';
  try {
    const res = await window.api.deleteUser(pending.id);
    document.getElementById('modal-confirm-user-delete').classList.remove('open');
    showToast(res.message || `Usuario '${pending.username}' eliminado.`);
    state.userPendingDelete = null;
    loadAdminUsers();
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.innerText = 'Sí, Eliminar';
  }
}

// --- Admin ---
async function loadAdminUsers() {
  try {
    const [users, config] = await Promise.all([
      window.api.getUsers(),
      window.api.getConfig()
    ]);

    const tbody = document.getElementById('tbody-admin-users');
    tbody.innerHTML = '';
    users.forEach(u => {
      // Sin botón Eliminar para uno mismo ni para el admin principal (el servidor también lo impide).
      const isSelf = u.username.toLowerCase() === (state.user?.username || '').toLowerCase();
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${u.id}</td>
        <td><strong>${escapeHtml(u.username)}</strong>${u.isPrincipal ? ' <span class="role-badge admin" title="No se puede eliminar, desactivar ni quitarle el rol Admin">Principal</span>' : ''}</td>
        <td>${escapeHtml(u.fullName)}</td>
        <td><span class="role-badge ${u.role === 'Admin' ? 'admin' : 'operator'}">${escapeHtml(u.role)}</span></td>
        <td>
          <span class="status-pill ${u.isActive ? 'success' : 'danger'}">${u.isActive ? 'Activo' : 'Inactivo'}</span>
          ${u.isLockedOut ? '<span class="status-pill danger" title="Bloqueada por intentos fallidos">Bloqueada</span>' : ''}
          ${u.mustChangePassword ? '<span class="status-pill warning" title="Debe cambiar la contraseña al ingresar">Clave temporal</span>' : ''}
        </td>
        <td>${escapeHtml(formatDate(u.lastLoginAt))}</td>
        <td>
          <button class="btn btn-sm btn-secondary" data-edit-user="${escapeHtml(JSON.stringify(u))}">Editar</button>
          ${isSelf || u.isPrincipal ? '' : `<button class="btn btn-sm btn-danger" data-delete-user="${u.id}" data-username="${escapeHtml(u.username)}">Eliminar</button>`}
        </td>
      `;
      tbody.appendChild(tr);
    });

    if (config.InputPath) document.getElementById('admin-cfg-input').value = config.InputPath;
    if (config.OutputDir) document.getElementById('admin-cfg-output').value = config.OutputDir;
  } catch (err) {
    showToast(err.message, 'error');
  }
}

window.onEditUser = function(user) {
  document.getElementById('modal-user-id').value = user.id;
  document.getElementById('modal-user-username').value = user.username;
  document.getElementById('modal-user-username').disabled = true;
  document.getElementById('modal-user-fullname').value = user.fullName;
  document.getElementById('modal-user-role').value = user.role;
  document.getElementById('modal-user-active').checked = user.isActive;
  // Admin principal: siempre activo y con rol Admin (solo se edita nombre y contraseña).
  document.getElementById('modal-user-role').disabled = !!user.isPrincipal;
  document.getElementById('modal-user-active').disabled = !!user.isPrincipal;
  document.getElementById('modal-user-password').value = '';
  document.getElementById('modal-user-password').placeholder = 'Dejar en blanco para no cambiar';
  document.getElementById('modal-user-unlock-group').hidden = !user.isLockedOut;
  document.getElementById('modal-user-unlock').checked = true;
  document.getElementById('modal-user-title').innerText = `Editar Usuario: ${user.username}`;
  document.getElementById('modal-user').classList.add('open');
};

async function onSaveUser(e) {
  e.preventDefault();
  const id = document.getElementById('modal-user-id').value;
  const username = document.getElementById('modal-user-username').value;
  const fullName = document.getElementById('modal-user-fullname').value;
  const role = document.getElementById('modal-user-role').value;
  const isActive = document.getElementById('modal-user-active').checked;
  const password = document.getElementById('modal-user-password').value;

  try {
    if (id) {
      const unlock = !document.getElementById('modal-user-unlock-group').hidden &&
        document.getElementById('modal-user-unlock').checked;
      await window.api.updateUser(id, { fullName, password: password || null, role, isActive, unlock });
      showToast('Usuario actualizado.');
    } else {
      if (!password) {
        showToast('La contraseña es requerida para nuevo usuario.', 'error');
        return;
      }
      await window.api.createUser({ username, fullName, password, role });
      showToast('Usuario creado exitosamente.');
    }
    document.getElementById('modal-user').classList.remove('open');
    loadAdminUsers();
  } catch (err) {
    showToast(err.message, 'error');
  }
}

// --- Bitácora de auditoría ---
const AUDIT_ACTIONS = {
  ingreso: ['Ingreso', 'success'],
  ingreso_fallido: ['Ingreso fallido', 'warning'],
  cuenta_bloqueada: ['Cuenta bloqueada', 'danger'],
  cambio_contrasena: ['Cambio de contraseña', 'success'],
  ocupacion_consulta: ['Consulta de persona', 'warning'],
  ocupacion_exporta: ['Exportación Ocupación', 'warning'],
  ocupacion_carga: ['Carga de Excel', 'success'],
  ocupacion_borra: ['Borrado de archivo', 'danger'],
  dtu_descarga: ['Descarga DTU', 'success']
};

async function loadAudit() {
  const tbody = document.getElementById('tbody-audit');
  try {
    const items = await window.api.getAudit(document.getElementById('audit-search').value.trim());
    if (!items.length) {
      tbody.innerHTML = '<tr><td colspan="5" class="ta-center c-dim">Sin registros.</td></tr>';
      return;
    }
    tbody.innerHTML = items.map(a => {
      const [label, cls] = AUDIT_ACTIONS[a.action] || [a.action, 'warning'];
      return `
        <tr>
          <td class="ws-nowrap">${escapeHtml(formatDate(a.at))}</td>
          <td><strong>${escapeHtml(a.username)}</strong></td>
          <td><span class="status-pill ${cls}">${escapeHtml(label)}</span></td>
          <td class="audit-detail">${escapeHtml(a.detail)}</td>
          <td class="c-dim">${escapeHtml(a.ip || '')}</td>
        </tr>`;
    }).join('');
  } catch (err) {
    showToast(err.message, 'error');
  }
}

// --- Event Listeners (Administración) ---
function initAdminListeners() {
  document.addEventListener('click', (e) => {
    const editBtn = e.target.closest('[data-edit-user]');
    if (editBtn) {
      try {
        window.onEditUser(JSON.parse(editBtn.dataset.editUser));
      } catch (err) {
        showToast('No se pudo abrir el usuario.', 'error');
      }
      return;
    }

    const userDeleteBtn = e.target.closest('[data-delete-user]');
    if (userDeleteBtn) {
      state.userPendingDelete = {
        id: Number(userDeleteBtn.dataset.deleteUser),
        username: userDeleteBtn.dataset.username
      };
      document.getElementById('confirm-user-name').innerText = userDeleteBtn.dataset.username;
      document.getElementById('modal-confirm-user-delete').classList.add('open');
    }
  });

  document.getElementById('btn-admin-backup').addEventListener('click', onRunBackup);

  // Admin New User
  document.getElementById('btn-admin-new-user')?.addEventListener('click', () => {
    document.getElementById('form-user-modal').reset();
    document.getElementById('modal-user-id').value = '';
    const usernameInput = document.getElementById('modal-user-username');
    usernameInput.disabled = false;
    usernameInput.value = '';
    document.getElementById('modal-user-role').disabled = false;
    document.getElementById('modal-user-active').disabled = false;
    document.getElementById('modal-user-password').placeholder = '••••••••';
    document.getElementById('modal-user-title').innerText = 'Nuevo Usuario';
    document.getElementById('modal-user-unlock-group').hidden = true;
    document.getElementById('modal-user').classList.add('open');
  });

  document.getElementById('form-user-modal')?.addEventListener('submit', onSaveUser);

  // Admin Clear Jobs
  document.getElementById('btn-admin-clear-jobs')?.addEventListener('click', () => {
    document.getElementById('modal-confirm-clear').classList.add('open');
  });

  document.getElementById('btn-confirm-clear-jobs')?.addEventListener('click', async () => {
    const btn = document.getElementById('btn-confirm-clear-jobs');
    btn.disabled = true;
    btn.innerText = 'Limpiando...';
    try {
      const res = await window.api.clearJobs();
      document.getElementById('modal-confirm-clear').classList.remove('open');
      showToast(res.message || 'Historial de procesos limpiado.');
      loadDashboard();
      loadJobs();
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      btn.disabled = false;
      btn.innerText = 'Sí, Limpiar Historial';
    }
  });

  // Admin Save Config
  document.getElementById('btn-admin-save-config')?.addEventListener('click', async () => {
    const inputPath = document.getElementById('admin-cfg-input').value.trim();
    const outputDir = document.getElementById('admin-cfg-output').value.trim();
    if (!inputPath || !outputDir) {
      showToast('Ambas rutas son obligatorias.', 'error');
      return;
    }

    const btn = document.getElementById('btn-admin-save-config');
    btn.disabled = true;
    btn.innerText = 'Guardando...';
    try {
      await window.api.updateConfig({ InputPath: inputPath, OutputDir: outputDir });
      const [info, options] = await Promise.all([
        window.api.getPadronInfo(),
        window.api.getOptions()
      ]);
      state.padronInfo = info;
      state.filterOptions = options;
      renderPadronStatus();
      renderFilterChips();
      showToast('Configuración guardada correctamente.');
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      btn.disabled = false;
      btn.innerText = 'Guardar Configuración';
    }
  });

  document.getElementById('btn-confirm-user-delete')?.addEventListener('click', onConfirmUserDelete);
}

let auditSearchTimer = null;
document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('audit-search')?.addEventListener('input', () => {
    clearTimeout(auditSearchTimer);
    auditSearchTimer = setTimeout(loadAudit, 300);
  });
});

registerModule({
  name: 'admin',
  tabs: ['admin'],
  init: initAdminListeners,
  onShow() {
    if (state.user?.role !== 'Admin') return;
    loadAdminUsers();
    loadBackups();
    loadAudit();
  }
});
