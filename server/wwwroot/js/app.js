// ==========================================================================
//  Application Logic - Usuarios Retirados DTU
// ==========================================================================

const state = {
  user: null,
  padronInfo: null,
  filterOptions: null,
  selectedEstados: new Set(['Terminated']),
  selectedSociedades: new Set(['BANCOLOMBIA']),
  activeTab: 'dashboard',
  jobs: [],
  previewRows: []
};

// --- Helper Functions ---
function showToast(message, type = 'success') {
  const container = document.getElementById('toast-container');
  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  toast.innerText = message;
  container.appendChild(toast);
  setTimeout(() => toast.remove(), 4000);
}

function formatDate(dateStr) {
  if (!dateStr) return 'N/A';
  const d = new Date(dateStr);
  return d.toLocaleString('es-CO', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
}

function parseInputDateToDmY(val) {
  if (!val) return '';
  // HTML date input gives YYYY-MM-DD
  const parts = val.split('-');
  if (parts.length === 3) {
    return `${parts[2]}/${parts[1]}/${parts[0]}`;
  }
  return val;
}

function dmyToInputDate(dmy) {
  if (!dmy) return '';
  const parts = dmy.split('/');
  if (parts.length === 3) {
    return `${parts[2]}-${parts[1]}-${parts[0]}`;
  }
  return '';
}

// --- App Initialization ---
document.addEventListener('DOMContentLoaded', async () => {
  initEventListeners();
  window.addEventListener('dtu:auth-error', () => showLoginScreen());

  if (window.api.token) {
    try {
      state.user = await window.api.getMe();
      showAppScreen();
    } catch (e) {
      showLoginScreen();
    }
  } else {
    showLoginScreen();
  }
});

function showLoginScreen() {
  document.getElementById('login-screen').style.display = 'flex';
  document.getElementById('app-screen').style.display = 'none';
}

function showAppScreen() {
  document.getElementById('login-screen').style.display = 'none';
  document.getElementById('app-screen').style.display = 'block';

  // Render user info
  document.getElementById('nav-user-fullname').innerText = state.user.fullName;
  const roleBadge = document.getElementById('nav-user-role');
  roleBadge.innerText = state.user.role;
  roleBadge.className = `role-badge ${state.user.role.toLowerCase()}`;

  // Hide or show admin tab
  const adminTabBtn = document.getElementById('tab-btn-admin');
  if (state.user.role === 'Admin') {
    adminTabBtn.style.display = 'flex';
  } else {
    adminTabBtn.style.display = 'none';
  }

  loadInitialData();
}

async function loadInitialData() {
  try {
    const [info, options] = await Promise.all([
      window.api.getPadronInfo(),
      window.api.getOptions()
    ]);
    state.padronInfo = info;
    state.filterOptions = options;

    renderPadronStatus();
    renderFilterChips();
    loadDashboard();
    loadJobs();
  } catch (err) {
    showToast(err.message, 'error');
  }
}

// --- Tab Switching ---
function switchTab(tabName) {
  state.activeTab = tabName;
  document.querySelectorAll('.nav-tab').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.tab === tabName);
  });
  document.querySelectorAll('.tab-pane').forEach(pane => {
    pane.classList.toggle('active', pane.id === `tab-${tabName}`);
  });

  if (tabName === 'dashboard') loadDashboard();
  if (tabName === 'historial') loadJobs();
  if (tabName === 'admin' && state.user.role === 'Admin') loadAdminUsers();
}

// --- Event Listeners ---
function initEventListeners() {
  // Login form
  document.getElementById('form-login').addEventListener('submit', async (e) => {
    e.preventDefault();
    const u = document.getElementById('login-username').value;
    const p = document.getElementById('login-password').value;
    const btn = document.getElementById('btn-login-submit');

    try {
      btn.disabled = true;
      btn.innerText = 'Iniciando...';
      const res = await window.api.login(u, p);
      state.user = { username: res.username, fullName: res.fullName, role: res.role };
      showAppScreen();
      showToast(`Bienvenido, ${res.fullName}!`);
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      btn.disabled = false;
      btn.innerText = 'Ingresar al Sistema';
    }
  });

  // Logout
  document.getElementById('btn-logout').addEventListener('click', () => {
    window.api.setToken(null);
    state.user = null;
    showLoginScreen();
    showToast('Sesión cerrada.');
  });

  // Nav Tabs
  document.querySelectorAll('.nav-tab').forEach(btn => {
    btn.addEventListener('click', () => switchTab(btn.dataset.tab));
  });

  // Quick Date suggestions
  document.getElementById('btn-date-suggested').addEventListener('click', () => {
    if (state.padronInfo?.suggestedDate) {
      document.getElementById('input-fecha-evento').value = dmyToInputDate(state.padronInfo.suggestedDate);
    }
  });

  document.getElementById('btn-date-today').addEventListener('click', () => {
    const today = new Date().toISOString().split('T')[0];
    document.getElementById('input-fecha-evento').value = today;
  });

  // Society quick buttons
  document.getElementById('btn-soc-all').addEventListener('click', () => {
    if (state.filterOptions) {
      state.filterOptions.sociedadesFijas.forEach(s => state.selectedSociedades.add(s));
      renderFilterChips();
    }
  });

  document.getElementById('btn-soc-none').addEventListener('click', () => {
    state.selectedSociedades.clear();
    renderFilterChips();
  });

  // Preview button
  document.getElementById('btn-preview').addEventListener('click', onGeneratePreview);

  // Process button
  document.getElementById('btn-process').addEventListener('click', onOpenConfirmProcess);
  document.getElementById('btn-confirm-process').addEventListener('click', onExecuteProcess);

  // Close modals
  document.querySelectorAll('.modal-close, .btn-modal-cancel').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.modal-overlay').forEach(m => m.classList.remove('open'));
    });
  });

  // History search filter
  document.getElementById('input-search-jobs').addEventListener('input', (e) => {
    const q = e.target.value.toLowerCase();
    renderJobsTable(state.jobs.filter(j => 
      j.createdByUsername.toLowerCase().includes(q) ||
      j.eventDate.includes(q) ||
      (j.selectedSocieties && j.selectedSocieties.toLowerCase().includes(q))
    ));
  });

  // Admin New User
  document.getElementById('btn-admin-new-user')?.addEventListener('click', () => {
    document.getElementById('modal-user-form').reset();
    document.getElementById('modal-user-id').value = '';
    document.getElementById('modal-user-title').innerText = 'Nuevo Usuario';
    document.getElementById('modal-user').classList.add('open');
  });

  document.getElementById('form-user-modal')?.addEventListener('submit', onSaveUser);
}

// --- Render Filter Chips ---
function renderFilterChips() {
  if (!state.filterOptions) return;

  // Estados
  const estadoContainer = document.getElementById('chips-estados');
  estadoContainer.innerHTML = '';
  state.filterOptions.estadosDisponibles.forEach(estado => {
    const isSelected = state.selectedEstados.has(estado);
    const chip = document.createElement('div');
    chip.className = `chip-toggle ${isSelected ? 'selected' : ''}`;
    chip.innerHTML = `<span class="chip-checkbox"></span><span>${estado}</span>`;
    chip.addEventListener('click', () => {
      if (state.selectedEstados.has(estado)) {
        state.selectedEstados.delete(estado);
      } else {
        state.selectedEstados.add(estado);
      }
      renderFilterChips();
    });
    estadoContainer.appendChild(chip);
  });

  // Sociedades
  const socContainer = document.getElementById('chips-sociedades');
  socContainer.innerHTML = '';
  state.filterOptions.sociedadesFijas.forEach(soc => {
    const isSelected = state.selectedSociedades.has(soc);
    const chip = document.createElement('div');
    chip.className = `chip-toggle ${isSelected ? 'selected' : ''}`;
    chip.innerHTML = `<span class="chip-checkbox"></span><span>${soc}</span>`;
    chip.addEventListener('click', () => {
      if (state.selectedSociedades.has(soc)) {
        state.selectedSociedades.delete(soc);
      } else {
        state.selectedSociedades.add(soc);
      }
      renderFilterChips();
    });
    socContainer.appendChild(chip);
  });

  // Set default suggested date in input if empty
  const dateInput = document.getElementById('input-fecha-evento');
  if (!dateInput.value && state.padronInfo?.suggestedDate) {
    dateInput.value = dmyToInputDate(state.padronInfo.suggestedDate);
  }
}

// --- Render Padron Status ---
function renderPadronStatus() {
  const info = state.padronInfo;
  if (!info) return;

  const statusBadge = document.getElementById('padron-status-badge');
  const sizeElem = document.getElementById('padron-size');
  const mtimeElem = document.getElementById('padron-mtime');
  const pathElem = document.getElementById('padron-path');
  const suggestedElem = document.getElementById('padron-suggested-date');

  if (info.exists) {
    statusBadge.className = 'status-pill success';
    statusBadge.innerHTML = '<span class="pulse-dot"></span> Padrón Disponible';
    sizeElem.innerText = `${info.sizeFormatted} (${info.totalColumns} columnas)`;
    mtimeElem.innerText = formatDate(info.lastModified);
    pathElem.innerText = info.path;
    suggestedElem.innerText = info.suggestedDate;
  } else {
    statusBadge.className = 'status-pill danger';
    statusBadge.innerHTML = '<span class="pulse-dot"></span> No Encontrado';
    sizeElem.innerText = '0 MB';
    mtimeElem.innerText = 'N/A';
    pathElem.innerText = info.path;
    suggestedElem.innerText = 'N/A';
  }
}

// --- Dashboard ---
async function loadDashboard() {
  try {
    const jobs = await window.api.getJobs(10);
    state.jobs = jobs;

    // Total retiros hoy
    const todayStr = new Date().toLocaleDateString('es-CO', { day: '2-digit', month: '2-digit', year: 'numeric' });
    const jobsToday = jobs.filter(j => j.eventDate === todayStr);
    const sumToday = jobsToday.reduce((acc, j) => acc + j.totalMatchedRows, 0);

    document.getElementById('kpi-today-count').innerText = sumToday;
    document.getElementById('kpi-total-jobs').innerText = jobs.length;

    // Recent activity table
    const tbody = document.getElementById('tbody-recent-jobs');
    tbody.innerHTML = '';
    if (jobs.length === 0) {
      tbody.innerHTML = '<tr><td colspan="6" style="text-align:center; color: var(--text-dim); padding: 2rem;">No hay ejecuciones registradas.</td></tr>';
      return;
    }

    jobs.slice(0, 5).forEach(job => {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${formatDate(job.createdAt)}</td>
        <td><strong>${job.eventDate}</strong></td>
        <td><span style="font-size:0.75rem; color:var(--text-muted);">${job.selectedSocieties || 'Todas'}</span></td>
        <td><strong style="color:var(--primary); font-size:1rem;">${job.totalMatchedRows}</strong></td>
        <td><span class="status-pill ${job.status === 'Completed' ? 'success' : 'danger'}">${job.status}</span></td>
        <td>
          ${job.dtuFileName ? `<button class="btn btn-sm btn-download" onclick="window.api.downloadJobFile(${job.id}, 'dtu', '${job.dtuFileName}')">DTU</button>` : ''}
          ${job.xlsxFileName ? `<button class="btn btn-sm btn-download" onclick="window.api.downloadJobFile(${job.id}, 'xlsx', '${job.xlsxFileName}')">XLSX</button>` : ''}
        </td>
      `;
      tbody.appendChild(tr);
    });
  } catch (err) {
    showToast(err.message, 'error');
  }
}

// --- Preview Execution ---
async function onGeneratePreview() {
  const dmy = parseInputDateToDmY(document.getElementById('input-fecha-evento').value);
  if (!dmy) {
    showToast('Seleccione una fecha de evento.', 'error');
    return;
  }
  if (state.selectedEstados.size === 0) {
    showToast('Seleccione al menos un estado.', 'error');
    return;
  }
  if (state.selectedSociedades.size === 0) {
    showToast('Seleccione al menos una sociedad.', 'error');
    return;
  }

  const btn = document.getElementById('btn-preview');
  try {
    btn.disabled = true;
    btn.innerText = 'Consultando...';

    const res = await window.api.getPreview(
      dmy,
      Array.from(state.selectedSociedades),
      Array.from(state.selectedEstados),
      100
    );

    document.getElementById('preview-modal-count').innerText = `${res.totalCoinciden} coincidencias en ${res.elapsedMs} ms (mostrando hasta 100)`;
    const tbody = document.getElementById('tbody-preview');
    tbody.innerHTML = '';

    if (res.rows.length === 0) {
      tbody.innerHTML = '<tr><td colspan="4" style="text-align:center; padding: 2rem; color:var(--text-dim);">No se encontraron coincidencias con los filtros aplicados.</td></tr>';
    } else {
      res.rows.forEach(r => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
          <td><span class="status-pill warning">${r.estado}</span></td>
          <td><strong>${r.documento}</strong></td>
          <td>${r.sociedad}</td>
          <td>${r.fechaEvento}</td>
        `;
        tbody.appendChild(tr);
      });
    }

    document.getElementById('modal-preview').classList.add('open');
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.innerText = '👁 Vista Previa';
  }
}

// --- Process Confirmation and Execution ---
async function onOpenConfirmProcess() {
  const dmy = parseInputDateToDmY(document.getElementById('input-fecha-evento').value);
  if (!dmy) {
    showToast('Seleccione una fecha de evento.', 'error');
    return;
  }
  if (state.selectedEstados.size === 0) {
    showToast('Seleccione al menos un estado.', 'error');
    return;
  }
  if (state.selectedSociedades.size === 0) {
    showToast('Seleccione al menos una sociedad.', 'error');
    return;
  }

  // Pre-calculate count for summary
  const btn = document.getElementById('btn-process');
  btn.disabled = true;
  btn.innerText = 'Verificando...';

  try {
    const preview = await window.api.getPreview(
      dmy,
      Array.from(state.selectedSociedades),
      Array.from(state.selectedEstados),
      1
    );

    document.getElementById('confirm-count').innerText = preview.totalCoinciden;
    document.getElementById('confirm-date').innerText = dmy;
    document.getElementById('confirm-estados').innerText = Array.from(state.selectedEstados).join(', ');
    document.getElementById('confirm-sociedades').innerText = Array.from(state.selectedSociedades).join(', ');

    const formats = [];
    if (document.getElementById('chk-opt-dtu').checked) formats.push('TXT DTU');
    if (document.getElementById('chk-opt-xlsx').checked) formats.push('XLSX');
    if (document.getElementById('chk-opt-tsv').checked) formats.push('TSV');
    document.getElementById('confirm-formats').innerText = formats.join(', ');

    document.getElementById('modal-confirm-process').classList.add('open');
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.innerText = '⚡ Procesar y Exportar';
  }
}

async function onExecuteProcess() {
  const dmy = parseInputDateToDmY(document.getElementById('input-fecha-evento').value);
  const emitDtu = document.getElementById('chk-opt-dtu').checked;
  const emitXlsx = document.getElementById('chk-opt-xlsx').checked;
  const emitTsv = document.getElementById('chk-opt-tsv').checked;

  const btnConfirm = document.getElementById('btn-confirm-process');
  btnConfirm.disabled = true;
  btnConfirm.innerText = 'Procesando...';

  try {
    const res = await window.api.processEmpleados({
      fechaEvento: dmy,
      sociedades: Array.from(state.selectedSociedades),
      estados: Array.from(state.selectedEstados),
      emitDtu,
      emitXlsx,
      emitTsv
    });

    document.getElementById('modal-confirm-process').classList.remove('open');
    showToast(`Éxito! ${res.totalCoinciden} registros exportados en ${res.elapsedMs} ms.`);

    // Switch to history tab to see and download the result
    switchTab('historial');
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btnConfirm.disabled = false;
    btnConfirm.innerText = 'Confirmar y Exportar';
  }
}

// --- Jobs History ---
async function loadJobs() {
  try {
    const jobs = await window.api.getJobs(100);
    state.jobs = jobs;
    renderJobsTable(jobs);
  } catch (err) {
    showToast(err.message, 'error');
  }
}

function renderJobsTable(jobs) {
  const tbody = document.getElementById('tbody-jobs');
  tbody.innerHTML = '';

  if (jobs.length === 0) {
    tbody.innerHTML = '<tr><td colspan="7" style="text-align:center; padding: 2rem; color:var(--text-dim);">No se encontraron procesos registrados.</td></tr>';
    return;
  }

  jobs.forEach(job => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${formatDate(job.createdAt)}</td>
      <td><strong>${job.eventDate}</strong></td>
      <td><span style="font-size:0.75rem; color:var(--text-muted);">${job.selectedSocieties || 'Todas'}</span></td>
      <td><strong style="color:var(--primary); font-size:1.05rem;">${job.totalMatchedRows}</strong></td>
      <td><span style="font-size:0.8rem; color:var(--text-muted);">${job.createdByUsername}</span></td>
      <td><span class="status-pill ${job.status === 'Completed' ? 'success' : 'danger'}">${job.status}</span></td>
      <td>
        <div style="display:flex; gap:0.4rem;">
          ${job.dtuFileName ? `<button class="btn btn-sm btn-download" title="Descargar DTU" onclick="window.api.downloadJobFile(${job.id}, 'dtu', '${job.dtuFileName}')">DTU</button>` : ''}
          ${job.xlsxFileName ? `<button class="btn btn-sm btn-download" title="Descargar Excel" onclick="window.api.downloadJobFile(${job.id}, 'xlsx', '${job.xlsxFileName}')">XLSX</button>` : ''}
          ${job.tsvFileName ? `<button class="btn btn-sm btn-download" title="Descargar TSV" onclick="window.api.downloadJobFile(${job.id}, 'tsv', '${job.tsvFileName}')">TSV</button>` : ''}
        </div>
      </td>
    `;
    tbody.appendChild(tr);
  });
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
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${u.id}</td>
        <td><strong>${u.username}</strong></td>
        <td>${u.fullName}</td>
        <td><span class="role-badge ${u.role.toLowerCase()}">${u.role}</span></td>
        <td><span class="status-pill ${u.isActive ? 'success' : 'danger'}">${u.isActive ? 'Activo' : 'Inactivo'}</span></td>
        <td>${formatDate(u.lastLoginAt)}</td>
        <td>
          <button class="btn btn-sm btn-secondary" onclick="onEditUser(${JSON.stringify(u).replace(/"/g, '&quot;')})">Editar</button>
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
  document.getElementById('modal-user-password').value = '';
  document.getElementById('modal-user-password').placeholder = 'Dejar en blanco para no cambiar';
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
      await window.api.updateUser(id, { fullName, password: password || null, role, isActive });
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
