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
  previewRows: [],
  vipList: [],
  vipPendingDeleteId: null
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

window.downloadJob = async function (jobId, format) {
  try {
    const fileName = await window.api.downloadJobFile(jobId, format);
    showToast(`Descargando ${fileName}...`);
  } catch (err) {
    showToast(err.message, 'error');
  }
};

function formatDate(dateStr) {
  if (!dateStr) return 'N/A';
  const d = new Date(dateStr);
  return d.toLocaleString('es-CO', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
}

// La fecha del evento se maneja como texto dd/mm/aaaa (el formato que espera la API),
// sin depender del idioma del navegador.
function parseDmy(val) {
  const m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec((val || '').trim());
  if (!m) return null;
  const day = Number(m[1]), month = Number(m[2]) - 1, year = Number(m[3]);
  const d = new Date(year, month, day);
  // Rechaza fechas inexistentes como 31/02/2026.
  if (d.getFullYear() !== year || d.getMonth() !== month || d.getDate() !== day) return null;
  return d;
}

function formatDmy(date) {
  const dd = String(date.getDate()).padStart(2, '0');
  const mm = String(date.getMonth() + 1).padStart(2, '0');
  return `${dd}/${mm}/${date.getFullYear()}`;
}

// Devuelve la fecha normalizada (dd/mm/aaaa) o '' si es inválida.
function parseInputDateToDmY(val) {
  const d = parseDmy(val);
  return d ? formatDmy(d) : '';
}

function readEventDate() {
  const input = document.getElementById('input-fecha-evento');
  const dmy = parseInputDateToDmY(input.value);
  if (!dmy) {
    input.classList.add('is-invalid');
    showToast(input.value.trim()
      ? 'Fecha inválida. Use el formato dd/mm/aaaa.'
      : 'Seleccione una fecha de evento.', 'error');
    return '';
  }
  input.value = dmy;
  input.classList.remove('is-invalid');
  return dmy;
}

function setEventDate(dmy) {
  const input = document.getElementById('input-fecha-evento');
  input.value = dmy || '';
  input.classList.remove('is-invalid');
}

// --- Date Picker (calendario propio: el <input type="date"> nativo muestra el
// formato del navegador y su ícono no se ve en el tema oscuro) ---
const DP_MONTHS = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio',
  'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
const DP_WEEKDAYS = ['Lu', 'Ma', 'Mi', 'Ju', 'Vi', 'Sá', 'Do'];

function initDatePicker(rootId) {
  const root = document.getElementById(rootId);
  const input = root.querySelector('input');
  const toggle = root.querySelector('[data-dp-toggle]');
  const popup = root.querySelector('.datepicker-popup');
  let viewYear, viewMonth;

  function render() {
    const selected = parseDmy(input.value);
    const todayStr = formatDmy(new Date());
    const selectedStr = selected ? formatDmy(selected) : '';
    // La semana empieza el lunes.
    const first = new Date(viewYear, viewMonth, 1);
    const start = new Date(viewYear, viewMonth, 1 - ((first.getDay() + 6) % 7));

    let html = `
      <div class="dp-header">
        <button type="button" class="dp-nav" data-dp-nav="-1" aria-label="Mes anterior">&#8249;</button>
        <span class="dp-title">${DP_MONTHS[viewMonth]} ${viewYear}</span>
        <button type="button" class="dp-nav" data-dp-nav="1" aria-label="Mes siguiente">&#8250;</button>
      </div>
      <div class="dp-grid">`;
    html += DP_WEEKDAYS.map(w => `<span class="dp-weekday">${w}</span>`).join('');
    for (let i = 0; i < 42; i++) {
      const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
      const dmy = formatDmy(d);
      const cls = ['dp-day'];
      if (d.getMonth() !== viewMonth) cls.push('is-other-month');
      if (dmy === todayStr) cls.push('is-today');
      if (dmy === selectedStr) cls.push('is-selected');
      html += `<button type="button" class="${cls.join(' ')}" data-dp-date="${dmy}"
        aria-label="${d.getDate()} de ${DP_MONTHS[d.getMonth()]} de ${d.getFullYear()}"
        ${dmy === selectedStr ? 'aria-pressed="true"' : ''}>${d.getDate()}</button>`;
    }
    html += '</div>';
    popup.innerHTML = html;
  }

  function open() {
    const base = parseDmy(input.value) || new Date();
    viewYear = base.getFullYear();
    viewMonth = base.getMonth();
    render();
    popup.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    (popup.querySelector('.is-selected') || popup.querySelector('.is-today') || popup.querySelector('.dp-day')).focus();
  }

  function close(returnFocus) {
    if (popup.hidden) return;
    popup.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    if (returnFocus) toggle.focus();
  }

  toggle.addEventListener('click', () => (popup.hidden ? open() : close(true)));

  popup.addEventListener('click', (e) => {
    const nav = e.target.closest('[data-dp-nav]');
    if (nav) {
      const d = new Date(viewYear, viewMonth + Number(nav.dataset.dpNav), 1);
      viewYear = d.getFullYear();
      viewMonth = d.getMonth();
      render();
      popup.querySelector(`[data-dp-nav="${nav.dataset.dpNav}"]`).focus();
      return;
    }
    const day = e.target.closest('[data-dp-date]');
    if (day) {
      setEventDate(day.dataset.dpDate);
      close(false);
      input.focus();
    }
  });

  // Teclado: flechas para moverse por los días, Escape para cerrar.
  popup.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') { e.preventDefault(); close(true); return; }
    const steps = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    const current = e.target.closest('[data-dp-date]');
    if (!current || !(e.key in steps)) return;
    e.preventDefault();
    const d = parseDmy(current.dataset.dpDate);
    d.setDate(d.getDate() + steps[e.key]);
    if (d.getMonth() !== viewMonth || d.getFullYear() !== viewYear) {
      viewYear = d.getFullYear();
      viewMonth = d.getMonth();
      render();
    }
    popup.querySelector(`[data-dp-date="${formatDmy(d)}"]`)?.focus();
  });

  // composedPath (calculado al despachar el evento) porque render() reemplaza el
  // botón pulsado y e.target ya no estaría dentro de root.
  document.addEventListener('click', (e) => {
    if (!e.composedPath().includes(root)) close(false);
  });

  // Escritura manual: solo dígitos y las barras se agregan solas (dd/mm/aaaa).
  input.addEventListener('input', (e) => {
    if (e.inputType && e.inputType.startsWith('delete')) return;
    const digits = input.value.replace(/\D/g, '').slice(0, 8);
    let out = digits.slice(0, 2);
    if (digits.length > 2) out += '/' + digits.slice(2, 4);
    if (digits.length > 4) out += '/' + digits.slice(4);
    if (digits.length === 2 || digits.length === 4) out += '/';
    input.value = out;
    input.classList.remove('is-invalid');
  });

  input.addEventListener('blur', () => {
    if (!input.value.trim()) return;
    const dmy = parseInputDateToDmY(input.value);
    if (dmy) input.value = dmy;
    input.classList.toggle('is-invalid', !dmy);
  });

  input.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown' && e.altKey) { e.preventDefault(); open(); }
    if (e.key === 'Escape') close(false);
  });
}

function escapeHtml(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
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

  // VIP: solo Admin puede gestionar (operador solo consulta)
  const vipNewBtn = document.getElementById('btn-vip-new');
  if (vipNewBtn) {
    vipNewBtn.style.display = state.user.role === 'Admin' ? 'inline-block' : 'none';
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
  if (tabName === 'vip') loadVipList();
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

  // Delegated actions (CSP strict: no inline event handlers)
  document.addEventListener('click', (e) => {
    const downloadBtn = e.target.closest('[data-download-job]');
    if (downloadBtn) {
      window.downloadJob(Number(downloadBtn.dataset.downloadJob), downloadBtn.dataset.format);
      return;
    }

    const editBtn = e.target.closest('[data-edit-user]');
    if (editBtn) {
      try {
        window.onEditUser(JSON.parse(editBtn.dataset.editUser));
      } catch (err) {
        showToast('No se pudo abrir el usuario.', 'error');
      }
      return;
    }

    const vipEditBtn = e.target.closest('[data-vip-edit]');
    if (vipEditBtn) {
      const id = Number(vipEditBtn.dataset.vipEdit);
      openVipModal(state.vipList.find(v => v.id === id) || null);
      return;
    }

    const vipDeleteBtn = e.target.closest('[data-vip-delete]');
    if (vipDeleteBtn) {
      const id = Number(vipDeleteBtn.dataset.vipDelete);
      const vip = state.vipList.find(v => v.id === id);
      state.vipPendingDeleteId = id;
      document.getElementById('confirm-vip-name').innerText = vip ? `${vip.fullName} (${vip.cedula})` : '';
      document.getElementById('modal-confirm-vip-delete').classList.add('open');
    }
  });

  document.getElementById('btn-goto-history')?.addEventListener('click', () => switchTab('historial'));

  // Quick Date suggestions
  initDatePicker('dp-fecha-evento');

  document.getElementById('btn-date-suggested').addEventListener('click', () => {
    if (state.padronInfo?.suggestedDate) {
      setEventDate(state.padronInfo.suggestedDate);
    }
  });

  document.getElementById('btn-date-today').addEventListener('click', () => {
    // Fecha local (toISOString daria el dia UTC: manana despues de las 7 p. m. en Colombia).
    setEventDate(formatDmy(new Date()));
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
      (j.createdByUsername || '').toLowerCase().includes(q) ||
      (j.createdByFullName || '').toLowerCase().includes(q) ||
      j.eventDate.includes(q) ||
      (j.selectedSocieties && j.selectedSocieties.toLowerCase().includes(q))
    ));
  });

  // Admin New User
  document.getElementById('btn-admin-new-user')?.addEventListener('click', () => {
    document.getElementById('form-user-modal').reset();
    document.getElementById('modal-user-id').value = '';
    const usernameInput = document.getElementById('modal-user-username');
    usernameInput.disabled = false;
    usernameInput.value = '';
    document.getElementById('modal-user-password').placeholder = '••••••••';
    document.getElementById('modal-user-title').innerText = 'Nuevo Usuario';
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

  // VIP list (Admin gestiona; operador solo consulta)
  document.getElementById('btn-vip-new')?.addEventListener('click', () => openVipModal(null));
  document.getElementById('form-vip-modal')?.addEventListener('submit', onSaveVip);
  document.getElementById('btn-confirm-vip-delete')?.addEventListener('click', onConfirmVipDelete);
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
    setEventDate(state.padronInfo.suggestedDate);
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
    statusBadge.innerHTML = '<span class="pulse-dot"></span> Archivo Disponible Para Procesar';
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
        <td>${escapeHtml(formatDate(job.createdAt))}</td>
        <td><strong>${escapeHtml(job.eventDate)}</strong></td>
        <td><span style="font-size:0.75rem; color:var(--text-muted);">${escapeHtml(job.selectedSocieties || 'Todas')}</span></td>
        <td><strong style="color:var(--primary); font-size:1rem;">${job.totalMatchedRows}</strong></td>
        <td><span class="status-pill ${job.status === 'Completed' ? 'success' : 'danger'}">${escapeHtml(job.status)}</span></td>
        <td>
          ${job.dtuFileName ? `<button class="btn btn-sm btn-download" data-download-job="${job.id}" data-format="dtu">DTU</button>` : ''}
          ${job.xlsxFileName ? `<button class="btn btn-sm btn-download" data-download-job="${job.id}" data-format="xlsx">XLSX</button>` : ''}
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
  const dmy = readEventDate();
  if (!dmy) return;
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

    const vipWarning = document.getElementById('preview-vip-warning');
    if (res.vipOmittedCount > 0) {
      const names = (res.vipOmittedRows || [])
        .map(r => `${r.cedula}${r.fullName ? ' (' + r.fullName + ')' : ''}`)
        .join(', ');
      vipWarning.innerHTML = `⚠ <strong>${res.vipOmittedCount} cédula(s) de la lista VIP omitida(s)</strong> — no se exportarán: ${escapeHtml(names)}`;
      vipWarning.style.display = 'block';
    } else {
      vipWarning.style.display = 'none';
      vipWarning.innerHTML = '';
    }

    const tbody = document.getElementById('tbody-preview');
    tbody.innerHTML = '';

    if (res.rows.length === 0) {
      tbody.innerHTML = '<tr><td colspan="4" style="text-align:center; padding: 2rem; color:var(--text-dim);">No se encontraron coincidencias con los filtros aplicados.</td></tr>';
    } else {
      res.rows.forEach(r => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
          <td><span class="status-pill warning">${escapeHtml(r.estado)}</span></td>
          <td><strong>${escapeHtml(r.documento)}</strong></td>
          <td>${escapeHtml(r.sociedad)}</td>
          <td>${escapeHtml(r.fechaEvento)}</td>
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
  const dmy = readEventDate();
  if (!dmy) return;
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

    const confirmVip = document.getElementById('confirm-vip');
    if (preview.vipOmittedCount > 0) {
      confirmVip.innerText = `⚠ ${preview.vipOmittedCount} cédula(s) de la lista VIP serán omitidas de los archivos.`;
      confirmVip.style.display = 'block';
    } else {
      confirmVip.style.display = 'none';
    }

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
    const vipMsg = res.vipOmittedCount > 0 ? ` (${res.vipOmittedCount} VIP omitidas)` : '';
    showToast(`Éxito! ${res.totalCoinciden} registros exportados en ${res.elapsedMs} ms${vipMsg}.`);

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
      <td>${escapeHtml(formatDate(job.createdAt))}</td>
      <td><strong>${escapeHtml(job.eventDate)}</strong></td>
      <td><span style="font-size:0.75rem; color:var(--text-muted);">${escapeHtml(job.selectedSocieties || 'Todas')}</span></td>
      <td>
        <strong style="color:var(--primary); font-size:1.05rem;">${job.totalMatchedRows}</strong>
        ${job.vipOmittedCount > 0 ? `<span class="status-pill warning" style="margin-left:0.4rem;" title="Omitidas: ${escapeHtml(job.vipOmittedDetails || '')}">VIP −${job.vipOmittedCount}</span>` : ''}
      </td>
      <td>
        <div style="font-size:0.8rem;">${escapeHtml(job.createdByFullName || job.createdByUsername)}</div>
        <div style="font-size:0.7rem; color:var(--text-dim);">${escapeHtml(job.createdByUsername)}</div>
      </td>
      <td><span class="status-pill ${job.status === 'Completed' ? 'success' : 'danger'}">${escapeHtml(job.status)}</span></td>
      <td>
        <div style="display:flex; gap:0.4rem;">
          ${job.dtuFileName ? `<button class="btn btn-sm btn-download" title="Descargar DTU" data-download-job="${job.id}" data-format="dtu">DTU</button>` : ''}
          ${job.xlsxFileName ? `<button class="btn btn-sm btn-download" title="Descargar Excel" data-download-job="${job.id}" data-format="xlsx">XLSX</button>` : ''}
          ${job.tsvFileName ? `<button class="btn btn-sm btn-download" title="Descargar TSV" data-download-job="${job.id}" data-format="tsv">TSV</button>` : ''}
        </div>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

// --- VIP list ---
async function loadVipList() {
  try {
    state.vipList = await window.api.getVipList();
    renderVipTable();
  } catch (err) {
    showToast(err.message, 'error');
  }
}

function renderVipTable() {
  const tbody = document.getElementById('tbody-vip');
  const isAdmin = state.user?.role === 'Admin';

  const accHeader = document.getElementById('th-vip-acciones');
  if (accHeader) accHeader.style.display = isAdmin ? '' : 'none';

  tbody.innerHTML = '';

  if (state.vipList.length === 0) {
    tbody.innerHTML = `<tr><td colspan="${isAdmin ? 5 : 4}" style="text-align:center; padding:2rem; color:var(--text-dim);">No hay cédulas en la lista VIP.</td></tr>`;
    return;
  }

  state.vipList.forEach(v => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><strong>${escapeHtml(v.cedula)}</strong></td>
      <td>${escapeHtml(v.fullName)}</td>
      <td>${escapeHtml(v.createdByUsername || '-')}</td>
      <td>${escapeHtml(formatDate(v.createdAt))}</td>
      ${isAdmin ? `<td>
        <div style="display:flex; gap:0.4rem;">
          <button class="btn btn-sm btn-secondary" data-vip-edit="${v.id}">Editar</button>
          <button class="btn btn-sm btn-danger" data-vip-delete="${v.id}">Quitar</button>
        </div>
      </td>` : ''}
    `;
    tbody.appendChild(tr);
  });
}

function openVipModal(vip) {
  document.getElementById('form-vip-modal').reset();
  document.getElementById('modal-vip-id').value = vip ? vip.id : '';
  document.getElementById('modal-vip-cedula').value = vip ? vip.cedula : '';
  document.getElementById('modal-vip-nombre').value = vip ? vip.fullName : '';
  document.getElementById('modal-vip-title').innerText = vip ? `Editar VIP: ${vip.cedula}` : 'Agregar VIP';
  document.getElementById('modal-vip').classList.add('open');
}

async function onSaveVip(e) {
  e.preventDefault();
  const id = document.getElementById('modal-vip-id').value;
  const cedula = document.getElementById('modal-vip-cedula').value.trim();
  const fullName = document.getElementById('modal-vip-nombre').value.trim();

  if (!cedula || !fullName) {
    showToast('Cédula y nombre son obligatorios.', 'error');
    return;
  }

  try {
    if (id) {
      await window.api.updateVip(id, { cedula, fullName });
      showToast('Registro VIP actualizado.');
    } else {
      await window.api.createVip({ cedula, fullName });
      showToast('Cédula agregada a la lista VIP.');
    }
    document.getElementById('modal-vip').classList.remove('open');
    loadVipList();
  } catch (err) {
    showToast(err.message, 'error');
  }
}

async function onConfirmVipDelete() {
  const id = state.vipPendingDeleteId;
  if (!id) return;

  const btn = document.getElementById('btn-confirm-vip-delete');
  btn.disabled = true;
  btn.innerText = 'Quitando...';
  try {
    const res = await window.api.deleteVip(id);
    document.getElementById('modal-confirm-vip-delete').classList.remove('open');
    showToast(res.message || 'Cédula quitada de la lista VIP.');
    state.vipPendingDeleteId = null;
    loadVipList();
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.innerText = 'Sí, Quitar';
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
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${u.id}</td>
        <td><strong>${escapeHtml(u.username)}</strong></td>
        <td>${escapeHtml(u.fullName)}</td>
        <td><span class="role-badge ${u.role === 'Admin' ? 'admin' : 'operator'}">${escapeHtml(u.role)}</span></td>
        <td><span class="status-pill ${u.isActive ? 'success' : 'danger'}">${u.isActive ? 'Activo' : 'Inactivo'}</span></td>
        <td>${escapeHtml(formatDate(u.lastLoginAt))}</td>
        <td>
          <button class="btn btn-sm btn-secondary" data-edit-user="${escapeHtml(JSON.stringify(u))}">Editar</button>
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
