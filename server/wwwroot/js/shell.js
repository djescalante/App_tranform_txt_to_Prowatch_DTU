// ==========================================================================
//  PW Extended App - carcasa: sesión, menú lateral, navegación por URL y
//  cambio de contraseña. Los módulos (dtu.js, ocupacion.js, admin.js) se
//  registran con registerModule() en core.js.
// ==========================================================================

// --- App Initialization ---
document.addEventListener('DOMContentLoaded', async () => {
  initShellListeners();
  App.modules.forEach(m => m.init?.());
  window.addEventListener('dtu:auth-error', () => showLoginScreen());
  window.addEventListener('popstate', onRouteChange);
  window.addEventListener('hashchange', onRouteChange);

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
  document.getElementById('login-screen').hidden = false;
  document.getElementById('app-screen').hidden = true;
}

function showAppScreen() {
  document.getElementById('login-screen').hidden = true;
  document.getElementById('app-screen').hidden = false;

  // Render user info
  document.getElementById('nav-user-fullname').innerText = state.user.fullName;
  const roleBadge = document.getElementById('nav-user-role');
  roleBadge.innerText = state.user.role;
  roleBadge.className = `role-badge ${state.user.role.toLowerCase()}`;

  // Menú y acciones solo para Admin (cualquier elemento con data-admin-only)
  const isAdmin = state.user.role === 'Admin';
  document.querySelectorAll('[data-admin-only]').forEach(el => { el.hidden = !isAdmin; });

  // Contraseña temporal: primero debe cambiarla (la API no responde otra cosa hasta entonces).
  if (state.user.mustChangePassword) {
    openPasswordModal(true);
    return;
  }
  startApp();
}

/** Arranca los módulos para el usuario actual y abre la pantalla de la URL (o la última). */
function startApp() {
  App.modules.forEach(m => m.onLogin?.());
  const tab = tabFromHash() || (isTabAvailable(state.activeTab) ? state.activeTab : 'dashboard');
  switchTab(tab, { replace: true });
}

// --- Cambio de contraseña ---
function openPasswordModal(forced) {
  const modal = document.getElementById('modal-password');
  if (forced) modal.dataset.forced = 'true'; else delete modal.dataset.forced;
  document.getElementById('modal-password-title').innerText = forced ? 'Debe cambiar su contraseña' : 'Cambiar contraseña';
  document.getElementById('modal-password-forced').hidden = !forced;
  document.getElementById('modal-password-close').hidden = forced;
  document.getElementById('modal-password-cancel').hidden = forced;
  document.getElementById('form-password').reset();
  modal.classList.add('open');
  setTimeout(() => document.getElementById('pwd-current').focus(), 50);
}

async function onChangePassword(e) {
  e.preventDefault();
  const current = document.getElementById('pwd-current').value;
  const next = document.getElementById('pwd-new').value;
  if (next !== document.getElementById('pwd-confirm').value) {
    showToast('La confirmación no coincide con la nueva contraseña.', 'error');
    return;
  }
  const btn = document.getElementById('btn-password-save');
  btn.disabled = true;
  try {
    const res = await window.api.changePassword(current, next);
    showToast(res.message || 'Contraseña actualizada.');
    const modal = document.getElementById('modal-password');
    const wasForced = !!modal.dataset.forced;
    delete modal.dataset.forced;
    modal.classList.remove('open');
    if (wasForced) {
      state.user.mustChangePassword = false;
      startApp();
    }
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
  }
}

// --- Navegación: cada pestaña tiene su dirección (#/proceso, #/oc-consulta…) ---
function isTabAvailable(tab) {
  if (!tab) return false;
  const btn = document.querySelector(`.nav-tab[data-tab="${CSS.escape(tab)}"]`);
  return !!btn && !btn.closest('[hidden]');
}

function tabFromHash() {
  const tab = decodeURIComponent(location.hash.replace(/^#\/?/, ''));
  return isTabAvailable(tab) ? tab : null;
}

function onRouteChange() {
  if (!state.user || state.user.mustChangePassword) return;
  const tab = tabFromHash();
  if (tab && tab !== state.activeTab) switchTab(tab, { updateUrl: false });
}

function switchTab(tabName, { updateUrl = true, replace = false } = {}) {
  state.activeTab = tabName;
  document.querySelectorAll('.nav-tab').forEach(btn => {
    const active = btn.dataset.tab === tabName;
    btn.classList.toggle('active', active);
    if (active) btn.setAttribute('aria-current', 'page'); else btn.removeAttribute('aria-current');
  });
  document.querySelectorAll('.tab-pane').forEach(pane => {
    pane.classList.toggle('active', pane.id === `tab-${tabName}`);
  });

  document.body.classList.remove('sidebar-open');
  document.getElementById('btn-menu')?.setAttribute('aria-expanded', 'false');

  const url = `#/${tabName}`;
  if (updateUrl && location.hash !== url) {
    if (replace) history.replaceState(null, '', url); else history.pushState(null, '', url);
  }

  moduleFor(tabName)?.onShow?.(tabName);
}

// --- Event Listeners (carcasa) ---
function initShellListeners() {
  document.getElementById('form-login').addEventListener('submit', async (e) => {
    e.preventDefault();
    const u = document.getElementById('login-username').value;
    const p = document.getElementById('login-password').value;
    const btn = document.getElementById('btn-login-submit');

    try {
      btn.disabled = true;
      btn.innerText = 'Iniciando...';
      const res = await window.api.login(u, p);
      state.user = { username: res.username, fullName: res.fullName, role: res.role, mustChangePassword: res.mustChangePassword };
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
    history.replaceState(null, '', location.pathname);
    showLoginScreen();
    showToast('Sesión cerrada.');
  });

  // Nav Tabs
  document.querySelectorAll('.nav-tab').forEach(btn => {
    btn.addEventListener('click', () => switchTab(btn.dataset.tab));
  });

  document.getElementById('btn-menu')?.addEventListener('click', (e) => {
    const open = document.body.classList.toggle('sidebar-open');
    e.currentTarget.setAttribute('aria-expanded', String(open));
  });

  // Close modals
  document.querySelectorAll('.modal-close, .btn-modal-cancel').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.modal-overlay').forEach(m => {
        if (!m.dataset.forced) m.classList.remove('open');
      });
    });
  });

  document.getElementById('btn-change-password').addEventListener('click', () => openPasswordModal(false));
  document.getElementById('form-password').addEventListener('submit', onChangePassword);
  window.addEventListener('dtu:must-change-password', () => {
    if (state.user) state.user.mustChangePassword = true;
    openPasswordModal(true);
  });
}
