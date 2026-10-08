// ==========================================================================
//  Módulo: Ocupación Edificios (PW Extended App)
//  Marcaciones de ProWatch cargadas desde los reportes diarios en Excel.
//  Usa las utilidades globales de app.js: showToast, escapeHtml, parseDmy,
//  formatDmy, initDatePicker, switchTab y state.
// ==========================================================================

(function () {
  const API = '/ocupacion';
  const SERIES_COLOR = '#3987e5'; // validado sobre el fondo oscuro de las tarjetas
  const nf = new Intl.NumberFormat('es-CO');

  /** Etiquetas del eje Y: 0, 5 mil, 10 mil, 1,5 M… (formato consistente en español). */
  function axisLabel(v) {
    if (v >= 1e6) return `${nf.format(+(v / 1e6).toFixed(1))} M`;
    if (v >= 1e3) return `${nf.format(+(v / 1e3).toFixed(1))} mil`;
    return nf.format(v);
  }

  const COLUMNS = [
    ['fecha', 'Fecha', 'fecha'],
    ['cedula', 'Cédula', 'cedula'],
    ['nombres', 'Nombres', 'nombres'],
    ['apellidos', 'Apellidos', 'apellidos'],
    ['empresa', 'Empresa', 'empresa'],
    ['ciudad', 'Ciudad', 'ciudad'],
    ['sede_administrativa', 'Sede', 'sede'],
    ['panel', 'Panel', 'panel'],
    ['first_swipe', 'Primera marcación', 'first_swipe'],
    ['last_swipe', 'Última marcación', 'last_swipe'],
    ['archivo', 'Archivo', null]
  ];

  const oc = {
    initialized: false,
    filtrosCargados: false,
    consultaHecha: false,
    page: 1,
    perPage: 50,
    pages: 1,
    sort: 'fecha',
    order: 'desc',
    lastStats: null,
    pendingDelete: null,
    resizeObserver: null
  };

  // ---------- Utilidades ----------

  /** dd/mm/aaaa -> aaaa-mm-dd ('' si está vacío; null si es inválida). */
  function dmyToIso(value) {
    if (!value || !value.trim()) return '';
    const d = parseDmy(value);
    if (!d) return null;
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${d.getFullYear()}-${mm}-${dd}`;
  }

  /** "aaaa-mm-dd[ hh:mm:ss]" -> "dd/mm/aaaa[ hh:mm:ss]". */
  function isoToDmy(value) {
    if (!value) return '';
    const m = /^(\d{4})-(\d{2})-(\d{2})(.*)$/.exec(value);
    return m ? `${m[3]}/${m[2]}/${m[1]}${m[4]}` : value;
  }

  /** Lee un rango de fechas de dos inputs; muestra error y devuelve null si es inválido. */
  function readRange(desdeId, hastaId) {
    const out = {};
    for (const [id, key] of [[desdeId, 'fechaDesde'], [hastaId, 'fechaHasta']]) {
      const input = document.getElementById(id);
      const iso = dmyToIso(input.value);
      if (iso === null) {
        input.classList.add('is-invalid');
        showToast('Fecha inválida. Use el formato dd/mm/aaaa.', 'error');
        return null;
      }
      input.classList.remove('is-invalid');
      if (iso) {
        input.value = isoToDmy(iso);
        out[key] = iso;
      }
    }
    if (out.fechaDesde && out.fechaHasta && out.fechaDesde > out.fechaHasta) {
      showToast('La fecha "Desde" es posterior a "Hasta".', 'error');
      return null;
    }
    return out;
  }

  const isAdmin = () => state.user?.role === 'Admin';

  function emptyRow(tbody, colspan, text) {
    tbody.innerHTML = `<tr><td colspan="${colspan}" class="oc-empty">${escapeHtml(text)}</td></tr>`;
  }

  // ---------- Dashboard ----------

  async function loadDashboard() {
    const range = readRange('oc-dash-desde', 'oc-dash-hasta');
    if (!range) return;

    const kpis = document.getElementById('oc-kpis');
    kpis.innerHTML = '<div class="oc-kpi"><div class="oc-kpi-label">Calculando…</div><div class="oc-kpi-value">--</div></div>';
    try {
      const stats = await window.api.get(`${API}/stats`, range);
      oc.lastStats = stats;
      renderDashboard(stats);
    } catch (err) {
      kpis.innerHTML = '';
      showToast(err.message, 'error');
    }
  }

  function renderDashboard(s) {
    const tiles = [
      ['Marcaciones', s.total],
      ['Personas', s.personas],
      ['Empresas', s.empresas],
      ['Ciudades', s.ciudades],
      ['Archivos cargados', s.archivos]
    ];
    document.getElementById('oc-kpis').innerHTML = tiles.map(([label, value]) => `
      <div class="oc-kpi">
        <div class="oc-kpi-value">${nf.format(value ?? 0)}</div>
        <div class="oc-kpi-label">${escapeHtml(label)}</div>
      </div>`).join('');

    document.getElementById('oc-dash-rango').textContent = s.fechaMin
      ? `Datos del ${isoToDmy(s.fechaMin)} al ${isoToDmy(s.fechaMax)}.`
      : 'Sin datos para el filtro aplicado.';
    document.getElementById('oc-dash-calculado').textContent =
      `Calculado el ${new Date(s.calculadoEn).toLocaleString('es-CO')} · se actualiza al cargar o borrar archivos.`;

    renderLineChart(document.getElementById('oc-chart-dia'), s.porDia);
    renderBars(document.getElementById('oc-top-empresas'), s.topEmpresas);
    renderBars(document.getElementById('oc-top-ciudades'), s.topCiudades);

    const tbody = document.getElementById('oc-top-cedulas');
    if (!s.topCedulas.length) {
      emptyRow(tbody, 4, 'Sin datos.');
    } else {
      tbody.innerHTML = s.topCedulas.map(c => `
        <tr>
          <td><strong>${escapeHtml(c.valor)}</strong></td>
          <td>${escapeHtml(c.nombre || '')}</td>
          <td class="num">${nf.format(c.total)}</td>
          <td class="num"><button type="button" class="btn btn-sm btn-secondary" data-oc-ver-cedula="${escapeHtml(c.valor)}">Ver marcaciones</button></td>
        </tr>`).join('');
    }
  }

  /** Barras horizontales (top 10) con el valor como etiqueta directa. */
  function renderBars(container, items) {
    if (!items || !items.length) {
      container.innerHTML = '<div class="oc-empty">Sin datos.</div>';
      return;
    }
    const max = Math.max(...items.map(i => i.total), 1);
    container.innerHTML = items.map(i => `
      <div class="oc-bar-row">
        <span class="oc-bar-label" title="${escapeHtml(i.valor)}">${escapeHtml(i.valor)}</span>
        <span class="oc-bar-track"><span class="oc-bar" style="display:block;width:${(i.total / max * 100).toFixed(2)}%"></span></span>
        <span class="oc-bar-value">${nf.format(i.total)}</span>
      </div>`).join('');
  }

  /**
   * Línea + área de marcaciones por día (SVG propio). Hover y teclado (flechas)
   * muestran fecha y valor en un tooltip; eje Y con 4 líneas guía tenues.
   */
  function renderLineChart(container, data) {
    container.innerHTML = '';
    container._ocData = data;
    if (!data || !data.length) {
      container.innerHTML = '<div class="oc-empty">Sin marcaciones para el filtro aplicado.</div>';
      return;
    }

    const W = Math.max(container.clientWidth, 320);
    const H = 260;
    const m = { top: 16, right: 16, bottom: 30, left: 56 };
    const iw = W - m.left - m.right;
    const ih = H - m.top - m.bottom;

    const times = data.map(d => new Date(d.fecha + 'T00:00:00').getTime());
    const t0 = times[0];
    const t1 = times[times.length - 1];
    const x = t => (t1 === t0 ? iw / 2 : (t - t0) / (t1 - t0) * iw) + m.left;

    const rawMax = Math.max(...data.map(d => d.total), 1);
    const step = niceStep(rawMax / 4);
    const yMax = Math.ceil(rawMax / step) * step;
    const y = v => m.top + ih - v / yMax * ih;

    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.setAttribute('tabindex', '0');
    svg.setAttribute('role', 'img');
    const peak = data.reduce((a, b) => (b.total > a.total ? b : a));
    svg.setAttribute('aria-label',
      `Marcaciones por día del ${isoToDmy(data[0].fecha)} al ${isoToDmy(data[data.length - 1].fecha)}. ` +
      `Máximo ${nf.format(peak.total)} el ${isoToDmy(peak.fecha)}. Use las flechas para recorrer los días.`);

    const el = (name, attrs) => {
      const n = document.createElementNS(ns, name);
      for (const [k, v] of Object.entries(attrs)) n.setAttribute(k, v);
      svg.appendChild(n);
      return n;
    };

    // Líneas guía y eje Y
    for (let v = 0; v <= yMax; v += step) {
      el('line', { x1: m.left, x2: W - m.right, y1: y(v), y2: y(v), stroke: 'rgba(255,255,255,0.07)', 'stroke-width': 1 });
      const t = el('text', { x: m.left - 8, y: y(v) + 4, 'text-anchor': 'end', class: 'oc-axis-label' });
      t.textContent = axisLabel(v);
    }

    // Eje X: ~6 fechas repartidas
    const ticks = Math.min(Math.max(2, Math.floor(iw / 95)), 6, data.length);
    for (let i = 0; i < ticks; i++) {
      const idx = ticks === 1 ? 0 : Math.round(i * (data.length - 1) / (ticks - 1));
      const t = el('text', {
        x: x(times[idx]), y: H - 8,
        'text-anchor': i === 0 ? 'start' : i === ticks - 1 ? 'end' : 'middle',
        class: 'oc-axis-label'
      });
      t.textContent = isoToDmy(data[idx].fecha).slice(0, 5);
    }

    const pts = data.map((d, i) => [x(times[i]), y(d.total)]);
    const line = pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join('');
    el('path', {
      d: `${line}L${pts[pts.length - 1][0].toFixed(1)},${y(0)}L${pts[0][0].toFixed(1)},${y(0)}Z`,
      fill: SERIES_COLOR, 'fill-opacity': 0.14, stroke: 'none'
    });
    el('path', { d: line, fill: 'none', stroke: SERIES_COLOR, 'stroke-width': 2, 'stroke-linejoin': 'round', 'stroke-linecap': 'round' });

    // Capa de interacción
    const cross = el('line', { y1: m.top, y2: m.top + ih, stroke: 'rgba(255,255,255,0.35)', 'stroke-width': 1, visibility: 'hidden' });
    const dot = el('circle', { r: 4, fill: SERIES_COLOR, stroke: '#111827', 'stroke-width': 2, visibility: 'hidden' });
    const hit = el('rect', { x: m.left, y: m.top, width: iw, height: ih, fill: 'transparent' });

    const tip = document.createElement('div');
    tip.className = 'oc-tooltip';
    tip.hidden = true;
    container.appendChild(svg);
    container.appendChild(tip);

    let current = data.length - 1;
    function show(i) {
      current = Math.max(0, Math.min(data.length - 1, i));
      const [px, py] = pts[current];
      cross.setAttribute('x1', px); cross.setAttribute('x2', px);
      dot.setAttribute('cx', px); dot.setAttribute('cy', py);
      cross.setAttribute('visibility', 'visible');
      dot.setAttribute('visibility', 'visible');
      tip.innerHTML = '';
      const strong = document.createElement('strong');
      strong.textContent = nf.format(data[current].total);
      const span = document.createElement('span');
      span.textContent = `marcaciones · ${isoToDmy(data[current].fecha)}`;
      tip.append(strong, span);
      tip.hidden = false;
      const scale = container.clientWidth / W;
      tip.style.left = `${Math.min(Math.max(px * scale, 70), container.clientWidth - 70)}px`;
      tip.style.top = `${py * scale}px`;
    }
    function hide() {
      cross.setAttribute('visibility', 'hidden');
      dot.setAttribute('visibility', 'hidden');
      tip.hidden = true;
    }
    function nearest(clientX) {
      const r = svg.getBoundingClientRect();
      const sx = (clientX - r.left) * (W / r.width);
      let best = 0;
      let bestD = Infinity;
      pts.forEach((p, i) => {
        const dd = Math.abs(p[0] - sx);
        if (dd < bestD) { bestD = dd; best = i; }
      });
      return best;
    }

    hit.addEventListener('pointermove', e => show(nearest(e.clientX)));
    hit.addEventListener('pointerleave', hide);
    svg.addEventListener('focus', () => show(current));
    svg.addEventListener('blur', hide);
    svg.addEventListener('keydown', e => {
      if (e.key === 'ArrowLeft') { e.preventDefault(); show(current - 1); }
      if (e.key === 'ArrowRight') { e.preventDefault(); show(current + 1); }
      if (e.key === 'Home') { e.preventDefault(); show(0); }
      if (e.key === 'End') { e.preventDefault(); show(data.length - 1); }
    });
  }

  function niceStep(raw) {
    const pow = Math.pow(10, Math.floor(Math.log10(Math.max(raw, 1))));
    const n = raw / pow;
    return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * pow;
  }

  // ---------- Consulta ----------

  async function ensureFiltros() {
    if (oc.filtrosCargados) return;
    try {
      const f = await window.api.get(`${API}/filtros`);
      fillSelect('oc-f-empresa', f.empresas, 'Todas');
      fillSelect('oc-f-ciudad', f.ciudades, 'Todas');
      fillSelect('oc-f-sede', f.sedes, 'Todas');
      fillSelect('oc-f-panel', f.paneles, 'Todos');
      oc.filtrosCargados = true;
    } catch (err) {
      showToast(err.message, 'error');
    }
  }

  function fillSelect(id, items, allLabel) {
    const sel = document.getElementById(id);
    const prev = sel.value;
    sel.innerHTML = '';
    const all = document.createElement('option');
    all.value = '';
    all.textContent = allLabel;
    sel.appendChild(all);
    for (const v of items) {
      const o = document.createElement('option');
      o.value = v;
      o.textContent = v;
      sel.appendChild(o);
    }
    sel.value = items.includes(prev) ? prev : '';
  }

  function consultaFiltros() {
    const range = readRange('oc-f-desde', 'oc-f-hasta');
    if (!range) return null;
    return {
      ...range,
      empresa: document.getElementById('oc-f-empresa').value,
      ciudad: document.getElementById('oc-f-ciudad').value,
      sede: document.getElementById('oc-f-sede').value,
      panel: document.getElementById('oc-f-panel').value,
      cedula: document.getElementById('oc-f-cedula').value.trim(),
      nombre: document.getElementById('oc-f-nombre').value.trim()
    };
  }

  function renderHeader() {
    document.getElementById('oc-thead').innerHTML = COLUMNS.map(([, label, sortKey]) => {
      if (!sortKey) return `<th>${escapeHtml(label)}</th>`;
      const active = oc.sort === sortKey;
      const ariaSort = active ? ` aria-sort="${oc.order === 'asc' ? 'ascending' : 'descending'}"` : '';
      const arrow = active ? (oc.order === 'asc' ? '▲' : '▼') : '';
      return `<th${ariaSort}><button type="button" class="oc-sort" data-oc-sort="${sortKey}">${escapeHtml(label)}<span aria-hidden="true">${arrow}</span></button></th>`;
    }).join('');
  }

  async function buscar(page = 1) {
    const filtros = consultaFiltros();
    if (!filtros) return;
    oc.page = page;
    renderHeader();
    const tbody = document.getElementById('oc-tbody');
    document.getElementById('oc-resumen').textContent = 'Buscando…';
    try {
      const res = await window.api.get(`${API}/registros`, {
        ...filtros, page: oc.page, perPage: oc.perPage, sort: oc.sort, order: oc.order
      });
      oc.consultaHecha = true;
      oc.pages = Math.max(res.pages, 1);
      document.getElementById('oc-resumen').textContent =
        `${nf.format(res.total)} marcaciones encontradas`;
      document.getElementById('oc-page-info').textContent = `Página ${nf.format(res.page)} de ${nf.format(oc.pages)}`;
      document.getElementById('oc-prev').disabled = res.page <= 1;
      document.getElementById('oc-next').disabled = res.page >= oc.pages;

      if (!res.items.length) {
        emptyRow(tbody, COLUMNS.length, 'No hay marcaciones con esos filtros.');
        return;
      }
      tbody.innerHTML = res.items.map(r => '<tr>' + COLUMNS.map(([key]) => {
        let v = r[key] ?? '';
        if (key === 'fecha' || key === 'first_swipe' || key === 'last_swipe') v = isoToDmy(v);
        return key === 'cedula' ? `<td><strong>${escapeHtml(v)}</strong></td>` : `<td>${escapeHtml(v)}</td>`;
      }).join('') + '</tr>').join('');
    } catch (err) {
      document.getElementById('oc-resumen').textContent = '--';
      showToast(err.message, 'error');
    }
  }

  function limpiar() {
    ['oc-f-desde', 'oc-f-hasta', 'oc-f-cedula', 'oc-f-nombre', 'oc-f-empresa', 'oc-f-ciudad', 'oc-f-sede', 'oc-f-panel']
      .forEach(id => {
        const e = document.getElementById(id);
        e.value = '';
        e.classList.remove('is-invalid');
      });
    buscar(1);
  }

  async function exportar(formato, btn) {
    const filtros = consultaFiltros();
    if (!filtros) return;
    const qs = new URLSearchParams({ formato });
    for (const [k, v] of Object.entries(filtros)) if (v) qs.append(k, v);

    const label = btn.textContent;
    btn.disabled = true;
    btn.textContent = 'Generando…';
    showToast('Generando el archivo; con muchos registros puede tardar.');
    try {
      await window.api.downloadFile(`${API}/export?${qs}`, null, `Ocupacion Edificios.${formato}`);
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      btn.disabled = false;
      btn.textContent = label;
    }
  }

  // ---------- Carga ----------

  function setFiles(files) {
    const input = document.getElementById('oc-files');
    if (files) {
      const dt = new DataTransfer();
      for (const f of files) dt.items.add(f);
      input.files = dt.files;
    }
    const n = input.files.length;
    document.getElementById('oc-files-label').textContent =
      n === 0 ? '' : n === 1 ? input.files[0].name : `${n} archivos seleccionados`;
  }

  async function subir() {
    const input = document.getElementById('oc-files');
    if (!input.files.length) {
      showToast('Seleccione al menos un archivo.', 'error');
      return;
    }
    const btn = document.getElementById('oc-btn-subir');
    btn.disabled = true;
    btn.textContent = `Subiendo ${input.files.length} archivo(s)…`;
    try {
      const res = await window.api.uploadFiles(`${API}/upload`, input.files);
      const estado = { cargado: ['success', 'Cargado'], ya_cargado: ['warning', 'Ya estaba cargado'], error: ['danger', 'Error'] };
      document.getElementById('oc-upload-result').innerHTML = `
        <div class="table-container"><table>
          <thead><tr><th>Archivo</th><th>Estado</th><th class="num">Leídas</th><th class="num">Insertadas</th><th class="num">Duplicadas</th><th class="num">Con error</th><th>Detalle</th></tr></thead>
          <tbody>${res.resultados.map(r => {
            const [cls, txt] = estado[r.estado] || ['warning', r.estado];
            return `<tr><td>${escapeHtml(r.nombre)}</td><td><span class="status-pill ${cls}">${escapeHtml(txt)}</span></td>
              <td class="num">${nf.format(r.filasLeidas)}</td><td class="num">${nf.format(r.filasInsertadas)}</td>
              <td class="num">${nf.format(r.filasDuplicadas)}</td><td class="num">${nf.format(r.filasError)}</td>
              <td>${escapeHtml(r.mensaje || '')}</td></tr>`;
          }).join('')}</tbody>
        </table></div>`;
      const cargados = res.resultados.filter(r => r.estado === 'cargado').length;
      showToast(`Carga finalizada: ${cargados} de ${res.resultados.length} archivo(s) cargados.`,
        res.resultados.some(r => r.estado === 'error') ? 'error' : 'success');
      if (cargados > 0) oc.filtrosCargados = false; // pueden haber valores nuevos
      input.value = '';
      setFiles();
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      btn.disabled = false;
      btn.textContent = 'Subir';
    }
  }

  // ---------- Archivos ----------

  async function loadArchivos() {
    const tbody = document.getElementById('oc-tbody-archivos');
    try {
      const res = await window.api.get(`${API}/archivos`);
      document.getElementById('oc-arch-count').textContent = nf.format(res.total);
      if (!res.items.length) {
        emptyRow(tbody, 8, 'No hay archivos cargados.');
        return;
      }
      const admin = isAdmin();
      tbody.innerHTML = res.items.map(a => `
        <tr>
          <td>${a.id}</td>
          <td><strong>${escapeHtml(a.nombre)}</strong></td>
          <td>${escapeHtml(isoToDmy(a.cargadoEn || ''))}</td>
          <td class="num">${nf.format(a.filasLeidas)}</td>
          <td class="num">${nf.format(a.filasInsertadas)}</td>
          <td class="num">${nf.format(a.filasDuplicadas)}</td>
          <td class="num">${nf.format(a.filasError)}</td>
          <td class="num">${admin ? `<button type="button" class="btn btn-sm btn-danger" data-oc-delete="${a.id}" data-oc-nombre="${escapeHtml(a.nombre)}" data-oc-filas="${a.filasInsertadas}">Eliminar</button>` : ''}</td>
        </tr>`).join('');
    } catch (err) {
      showToast(err.message, 'error');
    }
  }

  async function confirmarEliminar() {
    const p = oc.pendingDelete;
    if (!p) return;
    const btn = document.getElementById('oc-btn-confirm-eliminar');
    btn.disabled = true;
    btn.textContent = 'Eliminando…';
    try {
      const res = await window.api.request(`${API}/archivos/${p.id}`, { method: 'DELETE' });
      document.getElementById('modal-confirm-oc-archivo').classList.remove('open');
      showToast(res.message || 'Archivo eliminado.');
      oc.pendingDelete = null;
      oc.filtrosCargados = false;
      loadArchivos();
    } catch (err) {
      showToast(err.message, 'error');
    } finally {
      btn.disabled = false;
      btn.textContent = 'Sí, Eliminar';
    }
  }

  // ---------- Inicialización ----------

  function init() {
    if (oc.initialized) return;
    oc.initialized = true;

    ['dp-oc-dash-desde', 'dp-oc-dash-hasta', 'dp-oc-f-desde', 'dp-oc-f-hasta'].forEach(id => initDatePicker(id));

    document.getElementById('oc-dash-aplicar').addEventListener('click', loadDashboard);
    document.getElementById('oc-dash-todo').addEventListener('click', () => {
      document.getElementById('oc-dash-desde').value = '';
      document.getElementById('oc-dash-hasta').value = '';
      loadDashboard();
    });

    document.getElementById('oc-form-consulta').addEventListener('submit', e => {
      e.preventDefault();
      buscar(1);
    });
    document.getElementById('oc-btn-limpiar').addEventListener('click', limpiar);
    document.getElementById('oc-prev').addEventListener('click', () => buscar(Math.max(1, oc.page - 1)));
    document.getElementById('oc-next').addEventListener('click', () => buscar(Math.min(oc.pages, oc.page + 1)));

    const dz = document.getElementById('oc-dropzone');
    const fileInput = document.getElementById('oc-files');
    dz.addEventListener('click', () => fileInput.click());
    dz.addEventListener('keydown', e => {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); fileInput.click(); }
    });
    fileInput.addEventListener('change', () => setFiles());
    dz.addEventListener('dragover', e => { e.preventDefault(); dz.classList.add('is-dragover'); });
    dz.addEventListener('dragleave', () => dz.classList.remove('is-dragover'));
    dz.addEventListener('drop', e => {
      e.preventDefault();
      dz.classList.remove('is-dragover');
      const files = [...e.dataTransfer.files].filter(f => /\.(xlsx|xlsm)$/i.test(f.name));
      if (files.length < e.dataTransfer.files.length) showToast('Solo se aceptan archivos .xlsx o .xlsm.', 'error');
      setFiles(files);
    });
    document.getElementById('oc-btn-subir').addEventListener('click', subir);

    document.getElementById('oc-btn-refrescar').addEventListener('click', loadArchivos);
    document.getElementById('oc-btn-confirm-eliminar').addEventListener('click', confirmarEliminar);

    // Acciones delegadas (CSP: sin handlers inline)
    document.addEventListener('click', e => {
      const sortBtn = e.target.closest('[data-oc-sort]');
      if (sortBtn) {
        const key = sortBtn.dataset.ocSort;
        oc.order = oc.sort === key && oc.order === 'desc' ? 'asc' : 'desc';
        oc.sort = key;
        buscar(1);
        return;
      }
      const expBtn = e.target.closest('[data-oc-export]');
      if (expBtn) {
        exportar(expBtn.dataset.ocExport, expBtn);
        return;
      }
      const verBtn = e.target.closest('[data-oc-ver-cedula]');
      if (verBtn) {
        limpiarSinBuscar();
        document.getElementById('oc-f-cedula').value = verBtn.dataset.ocVerCedula;
        // Conserva el rango del dashboard en la consulta
        document.getElementById('oc-f-desde').value = document.getElementById('oc-dash-desde').value;
        document.getElementById('oc-f-hasta').value = document.getElementById('oc-dash-hasta').value;
        oc.consultaHecha = true;
        switchTab('oc-consulta');
        buscar(1);
        return;
      }
      const delBtn = e.target.closest('[data-oc-delete]');
      if (delBtn) {
        oc.pendingDelete = { id: Number(delBtn.dataset.ocDelete) };
        document.getElementById('oc-confirm-archivo').textContent = delBtn.dataset.ocNombre;
        document.getElementById('oc-confirm-filas').textContent = nf.format(Number(delBtn.dataset.ocFilas) || 0);
        document.getElementById('modal-confirm-oc-archivo').classList.add('open');
      }
    });

    // La gráfica se redibuja al cambiar el ancho (p. ej. al abrir/cerrar el menú)
    const chart = document.getElementById('oc-chart-dia');
    let lastWidth = 0;
    oc.resizeObserver = new ResizeObserver(() => {
      const w = chart.clientWidth;
      if (w && Math.abs(w - lastWidth) > 4 && chart._ocData) {
        lastWidth = w;
        renderLineChart(chart, chart._ocData);
      }
    });
    oc.resizeObserver.observe(chart);
  }

  function limpiarSinBuscar() {
    ['oc-f-desde', 'oc-f-hasta', 'oc-f-cedula', 'oc-f-nombre', 'oc-f-empresa', 'oc-f-ciudad', 'oc-f-sede', 'oc-f-panel']
      .forEach(id => { document.getElementById(id).value = ''; });
  }

  /** Lo llama switchTab (app.js) al abrir una pestaña "oc-*". */
  function onShow(tab) {
    init();
    if (tab === 'oc-dashboard') loadDashboard();
    if (tab === 'oc-consulta') {
      ensureFiltros();
      if (!oc.consultaHecha) buscar(1);
    }
    if (tab === 'oc-archivos') loadArchivos();
  }

  window.ocupacion = { onShow };
})();
