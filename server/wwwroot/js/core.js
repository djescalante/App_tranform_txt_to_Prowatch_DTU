// ==========================================================================
//  PW Extended App - núcleo: estado, utilidades, calendario, ventanas modales
//  y registro de módulos. Lo usan shell.js y cada módulo (dtu.js, ocupacion.js…).
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
  vipPendingDeleteId: null,
  userPendingDelete: null
};

// --- Registro de módulos ---
// Cada módulo se registra con:
//   name, tabs: ['x', ...] o owns(tab) -> bool, init() (una vez), onLogin() (cada ingreso),
//   onShow(tab) (al abrir una de sus pestañas).
const App = { modules: [] };

function registerModule(module) {
  App.modules.push(module);
}

/** Ícono del sprite de index.html (decorativo: el texto del botón lleva el significado). */
function icon(name) {
  return `<svg class="icon" aria-hidden="true"><use href="#i-${name}"></use></svg>`;
}

function moduleFor(tab) {
  return App.modules.find(m => (m.tabs && m.tabs.includes(tab)) || (m.owns && m.owns(tab)));
}

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
      input.value = day.dataset.dpDate;
      input.classList.remove('is-invalid');
      input.dispatchEvent(new Event('change', { bubbles: true }));
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

// --- Ventanas modales: Esc cierra, el foco queda dentro y vuelve al cerrar ---
(function initModalAccessibility() {
  const SELECTOR = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
  const focusables = modal => [...modal.querySelectorAll(SELECTOR)]
    .filter(el => !el.disabled && !el.closest('[hidden]') && el.offsetParent !== null);

  const observer = new MutationObserver(mutations => mutations.forEach(({ target }) => {
    const isOpen = target.classList.contains('open');
    if (isOpen && !target.ocWasOpen) {
      target.ocLastFocus = document.activeElement;
      setTimeout(() => {
        if (target.contains(document.activeElement)) return;
        const items = focusables(target);
        (items.find(el => el.matches('input, select, textarea')) || items[0])?.focus();
      }, 30);
    }
    if (!isOpen && target.ocWasOpen) {
      target.ocLastFocus?.focus?.();
    }
    target.ocWasOpen = isOpen;
  }));

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.modal-overlay').forEach(modal => {
      const content = modal.querySelector('.modal-content');
      content?.setAttribute('role', 'dialog');
      content?.setAttribute('aria-modal', 'true');
      const title = modal.querySelector('.modal-title');
      if (title && content) {
        title.id = title.id || `${modal.id}-titulo`;
        content.setAttribute('aria-labelledby', title.id);
      }
      observer.observe(modal, { attributes: true, attributeFilter: ['class'] });
    });
  });

  document.addEventListener('keydown', e => {
    const open = [...document.querySelectorAll('.modal-overlay.open')].pop();
    if (!open || e.defaultPrevented) return;
    if (e.key === 'Escape' && !open.dataset.forced) {
      e.preventDefault();
      open.classList.remove('open');
      return;
    }
    if (e.key === 'Tab') {
      const items = focusables(open);
      if (!items.length) return;
      const first = items[0];
      const last = items[items.length - 1];
      if (!open.contains(document.activeElement)) {
        e.preventDefault();
        first.focus();
      } else if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  });
})();
