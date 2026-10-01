# ============================================================================
#  UsuariosRetirados.ps1  -  GUI WinForms (Windows PowerShell 5.1)
#  App portable "Usuarios Retirados DTU"
# ============================================================================

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:BaseDir   = Split-Path -Parent $PSScriptRoot
$script:MotorPath = Join-Path $PSScriptRoot 'Motor.ps1'
$script:ConfigPath = Join-Path $PSScriptRoot 'config.json'
$script:EstructuraPath = Join-Path $PSScriptRoot 'estructura.json'
$script:Resumen   = $null
$script:UiBusy    = $false
$script:EstructuraAprobada = $true

. $script:MotorPath

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date).ToString('HH:mm:ss'), $Message
    $script:txtLog.AppendText($line + [Environment]::NewLine)
    $script:txtLog.SelectionStart = $script:txtLog.TextLength
    $script:txtLog.ScrollToCaret()
    [System.Windows.Forms.Application]::DoEvents()
}

# --- Catalogos de filtros (opciones fijas) ---
$script:EstadoOpciones = @(
    'Con terminación de contrato',
    'Terminated',
    'ReportNo-Show'
)
# Etiqueta visible -> valores reales del archivo (se expanden de forma transparente)
$script:EstadoMapa = @{
    'Con terminación de contrato' = @('Con terminación de contrato', 'Con terminacion de contrato')
    'Terminated'                  = @('Terminated')
    'ReportNo-Show'               = @('ReportNo-Show')
}
$script:EstadosDefault = @('Terminated')

$script:SociedadesFijas   = @('BANCOLOMBIA', 'NEQUI SA', 'VALORES BANCOLOMBIA', 'BANCA DE INVERSION BANCOLOMBIA')
$script:SociedadesDefault = @('BANCOLOMBIA')

function Get-SociedadesSeleccionadas {
    $sel = @()
    foreach ($item in $script:lstSoc.CheckedItems) { $sel += [string]$item }
    return $sel
}

function Get-EstadosSeleccionados {
    $valores = @()
    foreach ($item in $script:lstEstado.CheckedItems) {
        $clave = [string]$item
        if ($script:EstadoMapa.ContainsKey($clave)) { $valores += $script:EstadoMapa[$clave] }
    }
    return $valores
}

function Set-ListaMarcada {
    param($ListBox, [string[]]$Valores, [bool]$Marcar)
    for ($i = 0; $i -lt $ListBox.Items.Count; $i++) {
        $marcar = if ($Valores -and $Valores.Count -gt 0) { $Valores -contains [string]$ListBox.Items[$i] } else { $Marcar }
        $ListBox.SetItemChecked($i, $marcar)
    }
}

# ---------------------------------------------------------------------------
#  Construccion de la ventana
# ---------------------------------------------------------------------------
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Usuarios Retirados DTU'
$form.Size = New-Object System.Drawing.Size(860, 660)
$form.StartPosition = 'CenterScreen'
$form.MinimumSize = New-Object System.Drawing.Size(760, 600)
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

# --- Archivo ---
$lblArchivo = New-Object System.Windows.Forms.Label
$lblArchivo.Text = 'Archivo TXT de empleados:'
$lblArchivo.Location = New-Object System.Drawing.Point(15, 18)
$lblArchivo.AutoSize = $true

$script:txtArchivo = New-Object System.Windows.Forms.TextBox
$script:txtArchivo.Location = New-Object System.Drawing.Point(15, 40)
$script:txtArchivo.Size = New-Object System.Drawing.Size(680, 25)
$script:txtArchivo.ReadOnly = $true

$btnExaminar = New-Object System.Windows.Forms.Button
$btnExaminar.Text = 'Examinar...'
$btnExaminar.Location = New-Object System.Drawing.Point(705, 38)
$btnExaminar.Size = New-Object System.Drawing.Size(120, 28)

# --- Filtros ---
$grpFiltros = New-Object System.Windows.Forms.GroupBox
$grpFiltros.Text = 'Filtros'
$grpFiltros.Location = New-Object System.Drawing.Point(15, 78)
$grpFiltros.Size = New-Object System.Drawing.Size(810, 210)

$lblFecha = New-Object System.Windows.Forms.Label
$lblFecha.Text = 'Fecha del evento:'
$lblFecha.Location = New-Object System.Drawing.Point(15, 30)
$lblFecha.AutoSize = $true

$script:dtpFecha = New-Object System.Windows.Forms.DateTimePicker
$script:dtpFecha.Format = 'Custom'
$script:dtpFecha.CustomFormat = 'dd/MM/yyyy'
$script:dtpFecha.Location = New-Object System.Drawing.Point(130, 26)
$script:dtpFecha.Size = New-Object System.Drawing.Size(140, 25)
$script:dtpFecha.Enabled = $false

$lblFechaHint = New-Object System.Windows.Forms.Label
$lblFechaHint.Text = 'Por defecto: fecha de modificacion del archivo - 1 dia'
$lblFechaHint.ForeColor = [System.Drawing.Color]::DimGray
$lblFechaHint.Location = New-Object System.Drawing.Point(285, 30)
$lblFechaHint.AutoSize = $true

$lblEstado = New-Object System.Windows.Forms.Label
$lblEstado.Text = 'Estado (marque uno o varios):'
$lblEstado.Location = New-Object System.Drawing.Point(15, 62)
$lblEstado.AutoSize = $true

$script:lstEstado = New-Object System.Windows.Forms.CheckedListBox
$script:lstEstado.Location = New-Object System.Drawing.Point(15, 84)
$script:lstEstado.Size = New-Object System.Drawing.Size(370, 110)
$script:lstEstado.CheckOnClick = $true
foreach ($e in $script:EstadoOpciones) { [void]$script:lstEstado.Items.Add($e) }

$lblSoc = New-Object System.Windows.Forms.Label
$lblSoc.Text = 'Sociedad (marque una o varias):'
$lblSoc.Location = New-Object System.Drawing.Point(400, 62)
$lblSoc.AutoSize = $true

$script:lstSoc = New-Object System.Windows.Forms.CheckedListBox
$script:lstSoc.Location = New-Object System.Drawing.Point(400, 84)
$script:lstSoc.Size = New-Object System.Drawing.Size(300, 110)
$script:lstSoc.CheckOnClick = $true
foreach ($s in $script:SociedadesFijas) { [void]$script:lstSoc.Items.Add($s) }

$btnTodasSoc = New-Object System.Windows.Forms.Button
$btnTodasSoc.Text = 'Todas'
$btnTodasSoc.Location = New-Object System.Drawing.Point(710, 84)
$btnTodasSoc.Size = New-Object System.Drawing.Size(85, 28)

$btnNingunaSoc = New-Object System.Windows.Forms.Button
$btnNingunaSoc.Text = 'Ninguna'
$btnNingunaSoc.Location = New-Object System.Drawing.Point(710, 120)
$btnNingunaSoc.Size = New-Object System.Drawing.Size(85, 28)

# --- Salida ---
$grpSalida = New-Object System.Windows.Forms.GroupBox
$grpSalida.Text = 'Salida'
$grpSalida.Location = New-Object System.Drawing.Point(15, 298)
$grpSalida.Size = New-Object System.Drawing.Size(810, 105)

$script:chkXlsx = New-Object System.Windows.Forms.CheckBox
$script:chkXlsx.Text = 'XLSX (Excel)'
$script:chkXlsx.Checked = $true
$script:chkXlsx.Location = New-Object System.Drawing.Point(15, 25)
$script:chkXlsx.AutoSize = $true

$script:chkTsv = New-Object System.Windows.Forms.CheckBox
$script:chkTsv.Text = 'TSV (tabulado)'
$script:chkTsv.Location = New-Object System.Drawing.Point(140, 25)
$script:chkTsv.AutoSize = $true

$script:chkDtu = New-Object System.Windows.Forms.CheckBox
$script:chkDtu.Text = 'TXT DTU (obligatorio)'
$script:chkDtu.Checked = $true
$script:chkDtu.Location = New-Object System.Drawing.Point(265, 25)
$script:chkDtu.AutoSize = $true

$script:chkResumen = New-Object System.Windows.Forms.CheckBox
$script:chkResumen.Text = 'Mostrar resumen antes de exportar'
$script:chkResumen.Checked = $true
$script:chkResumen.Location = New-Object System.Drawing.Point(460, 25)
$script:chkResumen.AutoSize = $true

$lblCarpeta = New-Object System.Windows.Forms.Label
$lblCarpeta.Text = 'Carpeta destino:'
$lblCarpeta.Location = New-Object System.Drawing.Point(15, 60)
$lblCarpeta.AutoSize = $true

$script:txtSalida = New-Object System.Windows.Forms.TextBox
$script:txtSalida.Location = New-Object System.Drawing.Point(125, 57)
$script:txtSalida.Size = New-Object System.Drawing.Size(560, 25)
$script:txtSalida.Text = (Join-Path $script:BaseDir 'salidas')

$btnCarpeta = New-Object System.Windows.Forms.Button
$btnCarpeta.Text = 'Elegir...'
$btnCarpeta.Location = New-Object System.Drawing.Point(690, 55)
$btnCarpeta.Size = New-Object System.Drawing.Size(105, 28)

# --- Progreso ---
$script:progressBar = New-Object System.Windows.Forms.ProgressBar
$script:progressBar.Location = New-Object System.Drawing.Point(15, 412)
$script:progressBar.Size = New-Object System.Drawing.Size(810, 22)
$script:progressBar.Visible = $false

$script:lblStatus = New-Object System.Windows.Forms.Label
$script:lblStatus.Text = 'Seleccione un archivo para comenzar.'
$script:lblStatus.Location = New-Object System.Drawing.Point(15, 440)
$script:lblStatus.AutoSize = $true

# --- Botones ---
$script:btnPreview = New-Object System.Windows.Forms.Button
$script:btnPreview.Text = 'Vista previa'
$script:btnPreview.Location = New-Object System.Drawing.Point(15, 465)
$script:btnPreview.Size = New-Object System.Drawing.Size(130, 32)
$script:btnPreview.Enabled = $false

$script:btnExport = New-Object System.Windows.Forms.Button
$script:btnExport.Text = 'Exportar'
$script:btnExport.Location = New-Object System.Drawing.Point(155, 465)
$script:btnExport.Size = New-Object System.Drawing.Size(130, 32)
$script:btnExport.Enabled = $false

$btnCerrar = New-Object System.Windows.Forms.Button
$btnCerrar.Text = 'Cerrar'
$btnCerrar.Location = New-Object System.Drawing.Point(695, 465)
$btnCerrar.Size = New-Object System.Drawing.Size(130, 32)

# --- Log ---
$script:txtLog = New-Object System.Windows.Forms.TextBox
$script:txtLog.Location = New-Object System.Drawing.Point(15, 507)
$script:txtLog.Size = New-Object System.Drawing.Size(810, 70)
$script:txtLog.Multiline = $true
$script:txtLog.ReadOnly = $true
$script:txtLog.ScrollBars = 'Vertical'
$script:txtLog.BackColor = [System.Drawing.Color]::White

$form.Controls.AddRange(@(
    $lblArchivo, $script:txtArchivo, $btnExaminar, $grpFiltros, $grpSalida,
    $script:progressBar, $script:lblStatus, $script:btnPreview, $script:btnExport,
    $btnCerrar, $script:txtLog
))
$grpFiltros.Controls.AddRange(@($lblFecha, $script:dtpFecha, $lblFechaHint, $lblEstado, $script:lstEstado, $lblSoc, $script:lstSoc, $btnTodasSoc, $btnNingunaSoc))
$grpSalida.Controls.AddRange(@($script:chkXlsx, $script:chkTsv, $script:chkDtu, $script:chkResumen, $lblCarpeta, $script:txtSalida, $btnCarpeta))

# ---------------------------------------------------------------------------
#  Config
# ---------------------------------------------------------------------------
function Load-Config {
    if (Test-Path -LiteralPath $script:ConfigPath) {
        try {
            $cfg = Get-Content -LiteralPath $script:ConfigPath -Raw | ConvertFrom-Json
            if ($cfg.OutputDir) { $script:txtSalida.Text = $cfg.OutputDir }
            if ($cfg.InputPath -and (Test-Path -LiteralPath $cfg.InputPath)) {
                $script:txtArchivo.Text = $cfg.InputPath
            }
            if ($cfg.PSObject.Properties.Name -contains 'Estados' -and $cfg.Estados) {
                Set-ListaMarcada -ListBox $script:lstEstado -Valores @($cfg.Estados)
            }
            if ($cfg.PSObject.Properties.Name -contains 'Sociedades' -and $cfg.Sociedades) {
                Set-ListaMarcada -ListBox $script:lstSoc -Valores @($cfg.Sociedades)
            }
            if ($cfg.PSObject.Properties.Name -contains 'MostrarResumenExport') {
                $script:chkResumen.Checked = [bool]$cfg.MostrarResumenExport
            }
        } catch { Write-Log "No se pudo leer config.json: $($_.Exception.Message)" }
    }
}

function Set-Defaults {
    Set-ListaMarcada -ListBox $script:lstEstado -Valores $script:EstadosDefault
    Set-ListaMarcada -ListBox $script:lstSoc -Valores $script:SociedadesDefault
}

function Save-Config {
    try {
        $cfg = [pscustomobject]@{
            InputPath  = $script:txtArchivo.Text
            OutputDir  = $script:txtSalida.Text
            Estados    = @($script:lstEstado.CheckedItems | ForEach-Object { [string]$_ })
            Sociedades = @(Get-SociedadesSeleccionadas)
            MostrarResumenExport = [bool]$script:chkResumen.Checked
            LastUpdate = (Get-Date).ToString('s')
        }
        $cfg | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $script:ConfigPath -Encoding UTF8
    } catch { }
}

# ---------------------------------------------------------------------------
#  Operaciones
# ---------------------------------------------------------------------------
function Load-Resumen {
    $path = $script:txtArchivo.Text
    if (-not $path -or -not (Test-Path -LiteralPath $path)) { return }

    if (-not (Test-EstructuraArchivo -Path $path)) {
        $script:Resumen = $null
        $script:lblStatus.Text = 'Carga cancelada (estructura no confirmada).'
        return
    }

    Set-Busy $true
    $script:progressBar.Style = 'Marquee'
    $script:progressBar.Visible = $true
    $script:lblStatus.Text = 'Analizando archivo...'
    $script:Resumen = $null
    Write-Log "Analizando '$path'..."

    try {
        $onProg = {
            param($n)
            $script:lblStatus.Text = "Analizando... $n filas"
            [System.Windows.Forms.Application]::DoEvents()
        }
        $r = Get-EmpleadoResumen -Path $path -OnProgress $onProg
        $script:Resumen = $r

        $default = (Get-Item -LiteralPath $path).LastWriteTime.Date.AddDays(-1)
        $minFecha = if ($r.FechaMin) { $r.FechaMin } else { $default }
        $maxFecha = if ($r.FechaMax) { $r.FechaMax } else { $default }
        if ($default -lt $minFecha) { $minFecha = $default }
        if ($default -gt $maxFecha) { $maxFecha = $default }

        $script:dtpFecha.MinDate = $minFecha
        $script:dtpFecha.MaxDate = $maxFecha
        $script:dtpFecha.Value = $default

        Write-Log ("Rango de fechas en archivo: {0:dd/MM/yyyy} a {1:dd/MM/yyyy}" -f $r.FechaMin, $r.FechaMax)
        Write-Log ("Fecha por defecto (modificacion - 1 dia): {0:dd/MM/yyyy}" -f $default)
        Write-Log ("Listo: {0:N0} filas, {1} sociedades, {2} estados." -f $r.TotalFilas, $r.Sociedades.Count, $r.Estados.Count)
        if ($r.Malformed -gt 0) { Write-Log "Filas mal formadas omitidas: $($r.Malformed)" }
        $script:lblStatus.Text = 'Archivo cargado. Configure filtros y exporte.'

        $script:btnPreview.Enabled = $script:EstructuraAprobada
        $script:btnExport.Enabled = $script:EstructuraAprobada
    }
    catch {
        Write-Log "ERROR: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error') | Out-Null
        $script:lblStatus.Text = 'Error al analizar el archivo.'
    }
    finally {
        $script:progressBar.Visible = $false
        $script:progressBar.Style = 'Continuous'
        Set-Busy $false
    }
}

function Set-Busy {
    param([bool]$Busy)
    $script:UiBusy = $Busy
    $script:btnExport.Enabled = -not $Busy -and ($null -ne $script:Resumen) -and $script:EstructuraAprobada
    $script:btnPreview.Enabled = -not $Busy -and ($null -ne $script:Resumen) -and $script:EstructuraAprobada
    $btnExaminar.Enabled = -not $Busy
    $btnCerrar.Enabled = -not $Busy
    $script:dtpFecha.Enabled = -not $Busy
    $script:lstEstado.Enabled = -not $Busy
    $script:lstSoc.Enabled = -not $Busy
    $form.Cursor = if ($Busy) { 'WaitCursor' } else { 'Default' }
    [System.Windows.Forms.Application]::DoEvents()
}

function Set-TodasSoc {
    param([bool]$Marcar)
    for ($i = 0; $i -lt $script:lstSoc.Items.Count; $i++) {
        $script:lstSoc.SetItemChecked($i, $Marcar)
    }
}

function Get-FechaFiltro {
    return $script:dtpFecha.Value.ToString('dd/MM/yyyy')
}

function Show-EstructuraDialog {
    param($Comparacion, [bool]$PermitirContinuar)

    $detalle = New-Object System.Text.StringBuilder
    [void]$detalle.AppendLine("Total de columnas -> base: $($Comparacion.TotalEsperado) | archivo: $($Comparacion.TotalActual)")
    [void]$detalle.AppendLine('')
    [void]$detalle.AppendLine("Faltantes (en la base, no en el archivo):")
    if ($Comparacion.Faltantes.Count -eq 0) { [void]$detalle.AppendLine('  (ninguna)') }
    else { foreach ($x in $Comparacion.Faltantes) { [void]$detalle.AppendLine("  - $x") } }
    [void]$detalle.AppendLine('')
    [void]$detalle.AppendLine("Extra (en el archivo, no en la base):")
    if ($Comparacion.Extra.Count -eq 0) { [void]$detalle.AppendLine('  (ninguna)') }
    else { foreach ($x in $Comparacion.Extra) { [void]$detalle.AppendLine("  - $x") } }
    [void]$detalle.AppendLine('')
    [void]$detalle.AppendLine("Reordenadas:")
    if ($Comparacion.Reordenadas.Count -eq 0) { [void]$detalle.AppendLine('  (ninguna)') }
    else { foreach ($x in $Comparacion.Reordenadas) { [void]$detalle.AppendLine("  - $x") } }
    if ($Comparacion.RequeridasFaltantes.Count -gt 0) {
        [void]$detalle.AppendLine('')
        [void]$detalle.AppendLine("CRITICO - Requeridas faltantes: $($Comparacion.RequeridasFaltantes -join ', ')")
    }

    $f = New-Object System.Windows.Forms.Form
    $f.Text = 'Estructura del archivo'
    $f.Size = New-Object System.Drawing.Size(640, 480)
    $f.StartPosition = 'CenterParent'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false

    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = 'La estructura del archivo no coincide con la base guardada:'
    $lbl.Location = New-Object System.Drawing.Point(12, 12)
    $lbl.AutoSize = $true

    $txt = New-Object System.Windows.Forms.TextBox
    $txt.Multiline = $true; $txt.ReadOnly = $true; $txt.ScrollBars = 'Vertical'
    $txt.WordWrap = $false
    $txt.Font = New-Object System.Drawing.Font('Consolas', 9)
    $txt.Location = New-Object System.Drawing.Point(12, 40)
    $txt.Size = New-Object System.Drawing.Size(600, 340)
    $txt.Text = $detalle.ToString()

    $btnContinuar = New-Object System.Windows.Forms.Button
    $btnContinuar.Text = 'Continuar'
    $btnContinuar.Location = New-Object System.Drawing.Point(332, 392)
    $btnContinuar.Size = New-Object System.Drawing.Size(90, 30)
    $btnContinuar.Enabled = $PermitirContinuar

    $btnActualizar = New-Object System.Windows.Forms.Button
    $btnActualizar.Text = 'Actualizar estructura base'
    $btnActualizar.Location = New-Object System.Drawing.Point(428, 392)
    $btnActualizar.Size = New-Object System.Drawing.Size(184, 30)

    $btnCancelar = New-Object System.Windows.Forms.Button
    $btnCancelar.Text = 'Cancelar'
    $btnCancelar.Location = New-Object System.Drawing.Point(12, 392)
    $btnCancelar.Size = New-Object System.Drawing.Size(90, 30)

    $script:estructuraAccion = 'Cancelar'
    $btnContinuar.Add_Click({ $script:estructuraAccion = 'Continuar'; $f.Close() })
    $btnActualizar.Add_Click({ $script:estructuraAccion = 'Actualizar'; $f.Close() })
    $btnCancelar.Add_Click({ $script:estructuraAccion = 'Cancelar'; $f.Close() })

    $f.Controls.AddRange(@($lbl, $txt, $btnCancelar, $btnContinuar, $btnActualizar))
    $f.AcceptButton = $btnContinuar
    $f.CancelButton = $btnCancelar
    $f.ShowDialog($form) | Out-Null
    $f.Dispose()
    return $script:estructuraAccion
}

function Test-EstructuraArchivo {
    param([string]$Path)
    $script:EstructuraAprobada = $true

    try { $actual = Get-EmpleadoEncabezado -Path $Path }
    catch {
        Write-Log "ERROR al leer encabezado: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error') | Out-Null
        $script:EstructuraAprobada = $false
        return $false
    }

    $origen = Split-Path -Leaf $Path
    $base = Get-EstructuraBase -Path $script:EstructuraPath
    if (-not $base) {
        Save-EstructuraBase -Path $script:EstructuraPath -Columnas $actual -Origen $origen | Out-Null
        Write-Log ("No habia estructura base: se capturo la actual ({0} columnas)." -f $actual.Count)
        $cmp = Compare-Estructura -Esperada $actual -Actual $actual
    }
    else {
        $cmp = Compare-Estructura -Esperada $base.Columnas -Actual $actual
        if ($cmp.Igual) {
            Write-Log ("Estructura validada: {0} columnas, sin cambios." -f $actual.Count)
        }
        else {
            Write-Log ("ATENCION: estructura distinta (base {0} vs archivo {1} columnas)." -f $cmp.TotalEsperado, $cmp.TotalActual)
            $puede = ($cmp.RequeridasFaltantes.Count -eq 0)
            $accion = Show-EstructuraDialog -Comparacion $cmp -PermitirContinuar $puede
            if ($accion -eq 'Cancelar') {
                Write-Log 'Carga cancelada por el usuario (estructura no confirmada).'
                $script:EstructuraAprobada = $false
                return $false
            }
            elseif ($accion -eq 'Actualizar') {
                $conf = [System.Windows.Forms.MessageBox]::Show(
                    '¿Sobrescribir la estructura base con la del archivo actual? Se guardara un respaldo (estructura.bak.json).',
                    'Confirmar', 'YesNo', 'Question')
                if ($conf -eq 'Yes') {
                    $r = Save-EstructuraBase -Path $script:EstructuraPath -Columnas $actual -Origen $origen -Respaldo
                    Write-Log ("Estructura base actualizada ({0} columnas). Respaldo: {1}" -f $actual.Count, $r.Backup)
                }
                else {
                    Write-Log 'Actualizacion de estructura cancelada; se conserva la base anterior.'
                }
            }
            else {
                Write-Log 'Se continua con la estructura actual del archivo.'
            }
        }
    }

    if ($cmp.RequeridasFaltantes.Count -gt 0) {
        $falt = ($cmp.RequeridasFaltantes -join ', ')
        Write-Log "BLOQUEADO: faltan columnas requeridas ($falt)."
        [System.Windows.Forms.MessageBox]::Show(
            "Faltan columnas requeridas: $falt.`nLa exportacion queda bloqueada.",
            'Estructura', 'OK', 'Error') | Out-Null
        $script:EstructuraAprobada = $false
    }
    return $true
}

function Show-ResumenDialog {
    param(
        [int]$Coinciden,
        [string]$Fecha,
        [string[]]$Estados,
        [string[]]$Sociedades,
        [string]$Formatos,
        [string]$Carpeta,
        [string]$Archivo
    )

    $estTxt = if ($Estados -and $Estados.Count -gt 0) { $Estados -join ', ' } else { '(ninguno)' }
    $socTxt = if ($Sociedades -and $Sociedades.Count -gt 0) { $Sociedades -join ', ' } else { 'Todas' }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("Archivo origen  : $Archivo")
    [void]$sb.AppendLine("Fecha del evento: $Fecha")
    [void]$sb.AppendLine("Estados         : $estTxt")
    [void]$sb.AppendLine("Sociedades      : $socTxt")
    [void]$sb.AppendLine("Formatos        : $Formatos")
    [void]$sb.AppendLine("Carpeta destino : $Carpeta")

    $f = New-Object System.Windows.Forms.Form
    $f.Text = 'Confirmar exportacion'
    $f.Size = New-Object System.Drawing.Size(600, 330)
    $f.StartPosition = 'CenterParent'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false

    $lblTitulo = New-Object System.Windows.Forms.Label
    $lblTitulo.Text = "Filas a exportar: $Coinciden"
    $lblTitulo.Font = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
    $lblTitulo.Location = New-Object System.Drawing.Point(15, 15)
    $lblTitulo.AutoSize = $true

    $lblAviso = New-Object System.Windows.Forms.Label
    if ($Coinciden -eq 0) { $lblAviso.Text = 'No hay registros que coincidan con los filtros.'; $lblAviso.ForeColor = [System.Drawing.Color]::Firebrick }
    else { $lblAviso.Text = 'Revise los filtros antes de continuar.'; $lblAviso.ForeColor = [System.Drawing.Color]::DimGray }
    $lblAviso.Location = New-Object System.Drawing.Point(17, 45)
    $lblAviso.AutoSize = $true

    $txt = New-Object System.Windows.Forms.TextBox
    $txt.Multiline = $true; $txt.ReadOnly = $true
    $txt.Location = New-Object System.Drawing.Point(15, 72)
    $txt.Size = New-Object System.Drawing.Size(558, 170)
    $txt.Text = $sb.ToString()

    $btnExportar = New-Object System.Windows.Forms.Button
    $btnExportar.Text = 'Exportar'
    $btnExportar.Location = New-Object System.Drawing.Point(483, 252)
    $btnExportar.Size = New-Object System.Drawing.Size(90, 30)

    $btnCancelar = New-Object System.Windows.Forms.Button
    $btnCancelar.Text = 'Cancelar'
    $btnCancelar.Location = New-Object System.Drawing.Point(387, 252)
    $btnCancelar.Size = New-Object System.Drawing.Size(90, 30)

    $script:resumenAceptado = $false
    $btnExportar.Add_Click({ $script:resumenAceptado = $true; $f.Close() })
    $btnCancelar.Add_Click({ $script:resumenAceptado = $false; $f.Close() })

    $f.Controls.AddRange(@($lblTitulo, $lblAviso, $txt, $btnCancelar, $btnExportar))
    $f.AcceptButton = $btnExportar
    $f.CancelButton = $btnCancelar
    $f.ShowDialog($form) | Out-Null
    $f.Dispose()
    return $script:resumenAceptado
}

function Show-Preview {
    if (-not $script:Resumen) { return }
    if (-not $script:EstructuraAprobada) {
        [System.Windows.Forms.MessageBox]::Show('La estructura del archivo no fue confirmada. No se puede generar la vista previa.', 'Vista previa', 'OK', 'Warning') | Out-Null
        return
    }
    Set-Busy $true
    $script:progressBar.Style = 'Marquee'
    $script:progressBar.Visible = $true
    $script:lblStatus.Text = 'Generando vista previa...'
    Write-Log 'Generando vista previa...'
    try {
        $onProg = { param($n) $script:lblStatus.Text = "Vista previa... $n filas"; [System.Windows.Forms.Application]::DoEvents() }
        $res = Export-UsuariosRetirados -Path $script:txtArchivo.Text -OutputDir $script:txtSalida.Text `
            -FechaEvento (Get-FechaFiltro) -Sociedades (Get-SociedadesSeleccionadas) `
            -Estados (Get-EstadosSeleccionados) -PreviewLimit 200 -OnProgress $onProg

        Write-Log ("Vista previa: {0} coincidencias (mostrando {1})." -f $res.Coinciden, $res.Rows.Count)

        $pf = New-Object System.Windows.Forms.Form
        $pf.Text = ("Vista previa - {0} coincidencias" -f $res.Coinciden)
        $pf.Size = New-Object System.Drawing.Size(780, 460)
        $pf.StartPosition = 'CenterParent'
        $grid = New-Object System.Windows.Forms.DataGridView
        $grid.Dock = 'Fill'
        $grid.ReadOnly = $true
        $grid.AllowUserToAddRows = $false
        $grid.AutoSizeColumnsMode = 'AllCells'
        $grid.DataSource = $res.Rows
        $pf.Controls.Add($grid)
        $pf.ShowDialog($form) | Out-Null
        $pf.Dispose()
        $script:lblStatus.Text = 'Vista previa generada.'
    }
    catch {
        Write-Log "ERROR: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error') | Out-Null
    }
    finally {
        $script:progressBar.Visible = $false
        $script:progressBar.Style = 'Continuous'
        Set-Busy $false
    }
}

function Start-Export {
    if (-not $script:Resumen) { return }
    if (-not $script:EstructuraAprobada) {
        [System.Windows.Forms.MessageBox]::Show('La estructura del archivo no fue confirmada. No se puede exportar.', 'Exportar', 'OK', 'Warning') | Out-Null
        return
    }
    if (-not $script:chkXlsx.Checked -and -not $script:chkTsv.Checked -and -not $script:chkDtu.Checked) {
        [System.Windows.Forms.MessageBox]::Show('Seleccione al menos un formato de salida.', 'Aviso', 'OK', 'Warning') | Out-Null
        return
    }
    if ((Get-EstadosSeleccionados).Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show('Seleccione al menos un estado.', 'Aviso', 'OK', 'Warning') | Out-Null
        return
    }
    $sociedades = Get-SociedadesSeleccionadas
    if ($sociedades.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show('Marque al menos una sociedad (use "Todas" si quiere incluirlas todas).', 'Aviso', 'OK', 'Warning') | Out-Null
        return
    }
    $outDir = $script:txtSalida.Text
    if (-not $outDir) { [System.Windows.Forms.MessageBox]::Show('Indique la carpeta destino.', 'Aviso', 'OK', 'Warning') | Out-Null; return }

    $estados = Get-EstadosSeleccionados
    $fecha = Get-FechaFiltro

    Set-Busy $true
    $script:progressBar.Style = 'Continuous'
    $script:progressBar.Maximum = [Math]::Max(1, $script:Resumen.TotalFilas)
    $script:progressBar.Value = 0
    $script:progressBar.Visible = $true
    $script:lblStatus.Text = 'Buscando coincidencias...'
    Write-Log 'Buscando coincidencias...'
    try {
        $onProg = {
            param($n)
            if ($n -le $script:progressBar.Maximum) { $script:progressBar.Value = $n }
            $script:lblStatus.Text = "Buscando coincidencias... $n filas"
            [System.Windows.Forms.Application]::DoEvents()
        }

        $sel = Select-EmpleadoFilas -Path $script:txtArchivo.Text -FechaEvento $fecha `
            -Sociedades $sociedades -Estados $estados -OnProgress $onProg

        if ($script:chkResumen.Checked) {
            $formatos = @()
            if ($script:chkXlsx.Checked) { $formatos += 'XLSX' }
            if ($script:chkTsv.Checked) { $formatos += 'TSV' }
            if ($script:chkDtu.Checked) { $formatos += 'TXT DTU' }
            $estadosVisibles = @($script:lstEstado.CheckedItems | ForEach-Object { [string]$_ })

            $ok = Show-ResumenDialog -Coinciden $sel.Coinciden -Fecha $fecha -Estados $estadosVisibles `
                -Sociedades $sociedades -Formatos ($formatos -join ', ') -Carpeta $outDir `
                -Archivo (Split-Path -Leaf $script:txtArchivo.Text)

            if (-not $ok) {
                Write-Log 'Exportacion cancelada por el usuario.'
                $script:lblStatus.Text = 'Exportacion cancelada.'
                return
            }
        }

        Write-Log 'Escribiendo archivos...'
        $w = Write-UsuariosRetirados -Filas $sel.Filas -OutputDir $outDir -FechaTag $sel.FechaTag `
            -EmitXlsx:$script:chkXlsx.Checked -EmitTsv:$script:chkTsv.Checked -EmitDtu:$script:chkDtu.Checked

        Write-Log ("Exportacion finalizada: {0} filas coincidentes." -f $sel.Coinciden)
        foreach ($fl in $w.Files) { Write-Log "  -> $fl" }
        $script:lblStatus.Text = ("Listo. {0} registros exportados." -f $sel.Coinciden)
        Save-Config

        $msg = "Exportados {0} registros.{1}`n{2}" -f $sel.Coinciden, [Environment]::NewLine, ($w.Files -join [Environment]::NewLine)
        $r = [System.Windows.Forms.MessageBox]::Show(($msg + "`n`n¿Abrir la carpeta de salida?"), 'Completado', 'YesNo', 'Information')
        if ($r -eq 'Yes') { Start-Process explorer.exe -ArgumentList ('"' + $outDir + '"') }
    }
    catch {
        Write-Log "ERROR: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Error', 'OK', 'Error') | Out-Null
    }
    finally {
        $script:progressBar.Visible = $false
        Set-Busy $false
    }
}

# ---------------------------------------------------------------------------
#  Eventos
# ---------------------------------------------------------------------------
$btnExaminar.Add_Click({
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Title = 'Seleccione el archivo TXT de empleados'
    $dlg.Filter = 'Archivos de texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*'
    if ($script:txtArchivo.Text) { $dlg.InitialDirectory = Split-Path -Parent $script:txtArchivo.Text }
    if ($dlg.ShowDialog() -eq 'OK') {
        $script:txtArchivo.Text = $dlg.FileName
        $script:Resumen = $null
        $script:btnPreview.Enabled = $false
        $script:btnExport.Enabled = $false
        Load-Resumen
    }
})

$btnCarpeta.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Seleccione la carpeta destino'
    if ($script:txtSalida.Text -and (Test-Path -LiteralPath $script:txtSalida.Text)) { $dlg.SelectedPath = $script:txtSalida.Text }
    if ($dlg.ShowDialog() -eq 'OK') { $script:txtSalida.Text = $dlg.SelectedPath }
})

$btnTodasSoc.Add_Click({ Set-TodasSoc $true })
$btnNingunaSoc.Add_Click({ Set-TodasSoc $false })

$script:btnPreview.Add_Click({ Show-Preview })
$script:btnExport.Add_Click({ Start-Export })
$btnCerrar.Add_Click({ $form.Close() })

$form.Add_FormClosing({ Save-Config })

# ---------------------------------------------------------------------------
#  Arranque
# ---------------------------------------------------------------------------
$form.Add_Shown({
    try {
        Import-ImportExcelModule | Out-Null
        Write-Log 'Modulo ImportExcel cargado.'
    } catch {
        Write-Log "ADVERTENCIA: no se pudo cargar ImportExcel: $($_.Exception.Message)"
    }
    Set-Defaults
    Load-Config
    if ($script:txtArchivo.Text -and (Test-Path -LiteralPath $script:txtArchivo.Text)) {
        Load-Resumen
    }
})

[void]$form.ShowDialog()
$form.Dispose()
