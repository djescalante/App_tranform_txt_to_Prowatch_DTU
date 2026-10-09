// ==========================================================================
//  Módulo: Usuarios Retirados DTU (padrón -> archivos para ProWatch DTU)
//  Pestañas: dashboard, proceso, historial, vip.
// ==========================================================================

window.downloadJob = async function (jobId, format) {
  try {
    const fileName = await window.api.downloadJobFile(jobId, format);
    showToast(`Descargando ${fileName}...`);
  } catch (err) {
    showToast(err.message, 'error');
  }
};

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

async function loadDtuData() {
  try {
    const [info, options] = await Promise.all([
      window.api.getPadronInfo(),
      window.api.getOptions()
    ]);
    state.padronInfo = info;
    state.filterOptions = options;

    renderPadronStatus();
    renderFilterChips();
  } catch (err) {
    showToast(err.message, 'error');
  }
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
      tbody.innerHTML = '<tr><td class="ta-center c-dim p-200" colspan="6">No hay ejecuciones registradas.</td></tr>';
      return;
    }

    jobs.slice(0, 5).forEach(job => {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${escapeHtml(formatDate(job.createdAt))}</td>
        <td><strong>${escapeHtml(job.eventDate)}</strong></td>
        <td><span class="fs-075 c-muted">${escapeHtml(job.selectedSocieties || 'Todas')}</span></td>
        <td><strong class="c-primary fs-100">${job.totalMatchedRows}</strong></td>
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
      vipWarning.innerHTML = `${icon('alert')} <strong>${res.vipOmittedCount} cédula(s) de la lista VIP omitida(s)</strong> — no se exportarán: ${escapeHtml(names)}`;
      vipWarning.hidden = false;
    } else {
      vipWarning.hidden = true;
      vipWarning.innerHTML = '';
    }

    const tbody = document.getElementById('tbody-preview');
    tbody.innerHTML = '';

    if (res.rows.length === 0) {
      tbody.innerHTML = '<tr><td class="ta-center p-200 c-dim" colspan="6">No se encontraron coincidencias con los filtros aplicados.</td></tr>';
    } else {
      res.rows.forEach(r => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
          <td><span class="status-pill warning">${escapeHtml(r.estado)}</span></td>
          <td><strong>${escapeHtml(r.documento)}</strong></td>
          <td>${escapeHtml(r.nombres)}</td>
          <td>${escapeHtml(r.apellidos)}</td>
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
    btn.innerHTML = `${icon('eye')} Vista Previa`;
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
      confirmVip.innerHTML = `${icon('alert')} ${preview.vipOmittedCount} cédula(s) de la lista VIP serán omitidas de los archivos.`;
      confirmVip.hidden = false;
    } else {
      confirmVip.hidden = true;
    }

    document.getElementById('modal-confirm-process').classList.add('open');
  } catch (err) {
    showToast(err.message, 'error');
  } finally {
    btn.disabled = false;
    btn.innerHTML = `${icon('zap')} Procesar y Exportar`;
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
    tbody.innerHTML = '<tr><td class="ta-center p-200 c-dim" colspan="7">No se encontraron procesos registrados.</td></tr>';
    return;
  }

  jobs.forEach(job => {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${escapeHtml(formatDate(job.createdAt))}</td>
      <td><strong>${escapeHtml(job.eventDate)}</strong></td>
      <td><span class="fs-075 c-muted">${escapeHtml(job.selectedSocieties || 'Todas')}</span></td>
      <td>
        <strong class="c-primary fs-105">${job.totalMatchedRows}</strong>
        ${job.vipOmittedCount > 0 ? `<span class="status-pill warning ml-040" title="Omitidas: ${escapeHtml(job.vipOmittedDetails || '')}">VIP −${job.vipOmittedCount}</span>` : ''}
      </td>
      <td>
        <div class="fs-080">${escapeHtml(job.createdByFullName || job.createdByUsername)}</div>
        <div class="fs-070 c-dim">${escapeHtml(job.createdByUsername)}</div>
      </td>
      <td><span class="status-pill ${job.status === 'Completed' ? 'success' : 'danger'}">${escapeHtml(job.status)}</span></td>
      <td>
        <div class="d-flex gap-040">
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
  if (accHeader) accHeader.hidden = !isAdmin;

  tbody.innerHTML = '';

  if (state.vipList.length === 0) {
    tbody.innerHTML = `<tr><td class="ta-center p-200 c-dim" colspan="${isAdmin ? 5 : 4}">No hay cédulas en la lista VIP.</td></tr>`;
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
        <div class="d-flex gap-040">
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

// --- Event Listeners (DTU) ---
function initDtuListeners() {
  document.addEventListener('click', (e) => {
    const downloadBtn = e.target.closest('[data-download-job]');
    if (downloadBtn) {
      window.downloadJob(Number(downloadBtn.dataset.downloadJob), downloadBtn.dataset.format);
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

  // VIP list (Admin gestiona; operador solo consulta)
  document.getElementById('btn-vip-new')?.addEventListener('click', () => openVipModal(null));
  document.getElementById('form-vip-modal')?.addEventListener('submit', onSaveVip);
  document.getElementById('btn-confirm-vip-delete')?.addEventListener('click', onConfirmVipDelete);
}

registerModule({
  name: 'dtu',
  tabs: ['dashboard', 'proceso', 'historial', 'vip'],
  init: initDtuListeners,
  onLogin: loadDtuData,
  onShow(tab) {
    if (tab === 'dashboard') loadDashboard();
    if (tab === 'historial') loadJobs();
    if (tab === 'vip') loadVipList();
  }
});
