# ============================================================================
#  Motor.ps1  -  Motor de parsing, filtrado y exportacion
#  App portable "Usuarios Retirados DTU"  (compatible Windows PowerShell 5.1)
# ============================================================================

Add-Type -AssemblyName Microsoft.VisualBasic

$script:ColEstado = 'ESTADO'
$script:ColDoc    = 'DOCUMENTO'
$script:ColSoc    = 'NOMBRE SOCIEDAD'
$script:ColFecha  = 'FECHA EVENTO'

function Import-ImportExcelModule {
    [CmdletBinding()]
    param([string]$LibRoot)

    if (-not $LibRoot) {
        $LibRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib'
    }
    $manifest = Get-ChildItem -Path $LibRoot -Recurse -Filter 'ImportExcel.psd1' -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $manifest) { throw "No se encontro el modulo ImportExcel en '$LibRoot'." }
    Import-Module $manifest.FullName -Force -ErrorAction Stop
    return $manifest.FullName
}

function New-EmpleadoParser {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$EncodingIn = 'Windows-1252'
    )
    if (-not (Test-Path -LiteralPath $Path)) { throw "No existe el archivo: $Path" }

    $parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($Path, [System.Text.Encoding]::GetEncoding($EncodingIn))
    $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
    $parser.SetDelimiters(',')
    $parser.HasFieldsEnclosedInQuotes = $true
    $parser.TrimWhiteSpace = $false
    return $parser
}

function Get-EmpleadoColumnIndex {
    param([string[]]$Header)
    $idx = @{}
    for ($i = 0; $i -lt $Header.Count; $i++) {
        $name = $Header[$i].Trim()
        if (-not $idx.ContainsKey($name)) { $idx[$name] = $i }
    }
    foreach ($req in @($script:ColEstado, $script:ColDoc, $script:ColSoc, $script:ColFecha)) {
        if (-not $idx.ContainsKey($req)) { throw "No se encontro la columna '$req' en el encabezado." }
    }
    return $idx
}

function Get-EmpleadoEncabezado {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$EncodingIn = 'Windows-1252'
    )
    $parser = New-EmpleadoParser -Path $Path -EncodingIn $EncodingIn
    try {
        $h = $parser.ReadFields()
        return @($h | ForEach-Object { $_.Trim() })
    }
    finally { $parser.Close() }
}

function ConvertTo-FechaEvento {
    param([string]$Texto)
    $dt = [datetime]::MinValue
    $ok = [datetime]::TryParseExact(
        ([string]$Texto).Trim(),
        'dd/MM/yyyy',
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None,
        [ref]$dt)
    if ($ok) { return $dt }
    return $null
}

# ---------------------------------------------------------------------------
#  Estructura base (nombres + orden de columnas)
# ---------------------------------------------------------------------------
function Get-EstructuraBase {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $o = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            Columnas     = @($o.Columnas | ForEach-Object { [string]$_ })
            CapturadaDe  = $o.CapturadaDe
            FechaCaptura = $o.FechaCaptura
        }
    }
    catch { return $null }
}

function Save-EstructuraBase {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$Columnas,
        [string]$Origen = '',
        [switch]$Respaldo
    )
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $bak = ''
    if ($Respaldo -and (Test-Path -LiteralPath $Path)) {
        $bak = [System.IO.Path]::ChangeExtension($Path, '.bak.json')
        Copy-Item -LiteralPath $Path -Destination $bak -Force
    }

    $o = [pscustomobject]@{
        CapturadaDe   = $Origen
        FechaCaptura  = (Get-Date).ToString('s')
        TotalColumnas = $Columnas.Count
        Columnas      = @($Columnas)
    }
    $o | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding UTF8

    return [pscustomobject]@{ Path = $Path; Backup = $bak }
}

function Compare-Estructura {
    [CmdletBinding()]
    param(
        [string[]]$Esperada,
        [string[]]$Actual
    )
    if (-not $Esperada) { $Esperada = @() }
    if (-not $Actual)   { $Actual = @() }

    $cmp  = [System.StringComparer]::OrdinalIgnoreCase
    $eSet = New-Object 'System.Collections.Generic.HashSet[string]' $cmp
    foreach ($x in $Esperada) { if ($x) { [void]$eSet.Add($x.Trim()) } }
    $aSet = New-Object 'System.Collections.Generic.HashSet[string]' $cmp
    foreach ($x in $Actual) { if ($x) { [void]$aSet.Add($x.Trim()) } }

    $faltantes = @($eSet | Where-Object { -not $aSet.Contains($_) } | Sort-Object)
    $extra     = @($aSet | Where-Object { -not $eSet.Contains($_) } | Sort-Object)

    $idx = @{}
    for ($i = 0; $i -lt $Actual.Count; $i++) {
        $k = $Actual[$i].Trim()
        if (-not $idx.ContainsKey($k)) { $idx[$k] = $i }
    }
    $reordenadas = @()
    for ($i = 0; $i -lt $Esperada.Count; $i++) {
        $k = $Esperada[$i].Trim()
        if ($idx.ContainsKey($k) -and $idx[$k] -ne $i) { $reordenadas += $k }
    }
    $reordenadas = @($reordenadas | Select-Object -Unique)

    $requeridas = @($script:ColEstado, $script:ColDoc, $script:ColSoc, $script:ColFecha)
    $reqFalt    = @($requeridas | Where-Object { -not $aSet.Contains($_) })

    $igual = ($Esperada.Count -eq $Actual.Count)
    if ($igual) {
        for ($i = 0; $i -lt $Esperada.Count; $i++) {
            if (-not $Esperada[$i].Trim().Equals($Actual[$i].Trim(), [System.StringComparison]::OrdinalIgnoreCase)) {
                $igual = $false; break
            }
        }
    }

    return [pscustomobject]@{
        Igual               = $igual
        TotalEsperado       = $Esperada.Count
        TotalActual         = $Actual.Count
        Faltantes           = $faltantes
        Extra               = $extra
        Reordenadas         = $reordenadas
        RequeridasFaltantes = $reqFalt
    }
}

# ---------------------------------------------------------------------------
#  Resumen: filas, sociedades distintas, estados distintos, rango de fechas
# ---------------------------------------------------------------------------
function Get-EmpleadoResumen {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$EncodingIn = 'Windows-1252',
        [int]$ProgressEvery = 20000,
        [scriptblock]$OnProgress
    )

    $parser = New-EmpleadoParser -Path $Path -EncodingIn $EncodingIn
    try {
        $header = $parser.ReadFields()
        $idx    = Get-EmpleadoColumnIndex -Header $header

        $soc = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        $est = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        $min = $null; $max = $null; $total = 0; $mal = 0

        while (-not $parser.EndOfData) {
            try { $f = $parser.ReadFields() }
            catch [Microsoft.VisualBasic.FileIO.MalformedLineException] { $mal++; continue }

            $total++
            if ($f.Count -le $idx[$script:ColFecha]) { continue }

            [void]$soc.Add($f[$idx[$script:ColSoc]].Trim())
            [void]$est.Add($f[$idx[$script:ColEstado]].Trim())

            $dt = ConvertTo-FechaEvento $f[$idx[$script:ColFecha]]
            if ($dt) {
                if (-not $min -or $dt -lt $min) { $min = $dt }
                if (-not $max -or $dt -gt $max) { $max = $dt }
            }

            if ($OnProgress -and ($total % $ProgressEvery -eq 0)) { & $OnProgress $total }
        }

        return [pscustomobject]@{
            TotalFilas = $total
            Malformed  = $mal
            Sociedades = @($soc | Sort-Object)
            Estados    = @($est | Sort-Object)
            FechaMin   = $min
            FechaMax   = $max
        }
    }
    finally { $parser.Close() }
}

# ---------------------------------------------------------------------------
#  Seleccion (un solo barrido, cachea filas coincidentes en memoria)
# ---------------------------------------------------------------------------
function Select-EmpleadoFilas {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$EncodingIn = 'Windows-1252',
        [string]$FechaEvento,
        [string[]]$Sociedades,
        [string[]]$Estados = @('Terminated'),
        [int]$MaxFilas = 0,                 # 0 = sin limite (cachea todas las coincidencias)
        [int]$ProgressEvery = 20000,
        [scriptblock]$OnProgress
    )

    $fechaFiltro = $null
    if ($FechaEvento -and $FechaEvento.Trim()) { $fechaFiltro = $FechaEvento.Trim() }
    $fechaTag = if ($fechaFiltro) { $fechaFiltro -replace '/', '-' } else { 'Todas' }

    $socSet = $null
    if ($Sociedades -and $Sociedades.Count -gt 0 -and -not ($Sociedades -contains 'TODAS')) {
        $socSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($s in $Sociedades) { if ($s.Trim()) { [void]$socSet.Add($s.Trim()) } }
    }
    $estSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($e in $Estados) { if ($e) { [void]$estSet.Add($e.Trim()) } }

    $filas  = New-Object 'System.Collections.Generic.List[object]'
    $parser = New-EmpleadoParser -Path $Path -EncodingIn $EncodingIn
    $total = 0; $mal = 0; $match = 0

    try {
        $header = $parser.ReadFields()
        $idx    = Get-EmpleadoColumnIndex -Header $header
        $iE = $idx[$script:ColEstado]; $iD = $idx[$script:ColDoc]
        $iS = $idx[$script:ColSoc];    $iF = $idx[$script:ColFecha]

        while (-not $parser.EndOfData) {
            try { $f = $parser.ReadFields() }
            catch [Microsoft.VisualBasic.FileIO.MalformedLineException] { $mal++; continue }

            $total++
            if ($f.Count -le $iF) { continue }

            $estado = $f[$iE].Trim()
            $soc    = $f[$iS].Trim()
            $fecha  = $f[$iF].Trim()

            if (-not $estSet.Contains($estado))           { continue }
            if ($socSet -and -not $socSet.Contains($soc)) { continue }
            if ($fechaFiltro -and ($fecha -ne $fechaFiltro)) { continue }

            $match++
            if ($MaxFilas -le 0 -or $filas.Count -lt $MaxFilas) {
                [void]$filas.Add([string[]]@($estado, $f[$iD].Trim(), $soc, $fecha))
            }

            if ($OnProgress -and ($total % $ProgressEvery -eq 0)) { & $OnProgress $total }
        }
    }
    finally { $parser.Close() }

    return [pscustomobject]@{
        TotalFilas = $total
        Malformed  = $mal
        Coinciden  = $match
        Filas      = $filas
        FechaTag   = $fechaTag
    }
}

# ---------------------------------------------------------------------------
#  Escritura de salidas a partir de las filas ya seleccionadas
# ---------------------------------------------------------------------------
function Write-UsuariosRetirados {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IEnumerable]$Filas,
        [Parameter(Mandatory)][string]$OutputDir,
        [string]$FechaTag = 'Todas',
        [switch]$EmitXlsx,
        [switch]$EmitTsv,
        [switch]$EmitDtu
    )

    if (-not (Test-Path -LiteralPath $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

    $fileXlsx = Join-Path $OutputDir ("Usuarios Retirados {0}.xlsx" -f $FechaTag)
    $fileTsv  = Join-Path $OutputDir ("Usuarios Retirados {0}.tsv" -f $FechaTag)
    $fileDtu  = Join-Path $OutputDir ("Usuarios Retirados DTU al {0}.txt" -f $FechaTag)

    $encOut = New-Object System.Text.UTF8Encoding($true)
    $items  = @($Filas)
    $rows   = New-Object 'System.Collections.Generic.List[object]'
    $tsvWriter = $null; $dtuWriter = $null

    try {
        if ($EmitTsv) {
            $tsvWriter = New-Object System.IO.StreamWriter($fileTsv, $false, $encOut)
            $tsvWriter.WriteLine(($script:ColEstado, $script:ColDoc, $script:ColSoc, $script:ColFecha) -join "`t")
        }
        if ($EmitDtu) { $dtuWriter = New-Object System.IO.StreamWriter($fileDtu, $false, $encOut) }

        foreach ($f in $items) {
            $estado = [string]$f[0]; $doc = [string]$f[1]; $soc = [string]$f[2]; $fecha = [string]$f[3]
            if ($tsvWriter) { $tsvWriter.WriteLine(($estado, $doc, $soc, $fecha) -join "`t") }
            if ($dtuWriter) { $dtuWriter.WriteLine(($doc, $fecha, 'T') -join "`t") }
            if ($EmitXlsx) {
                [void]$rows.Add([pscustomobject]@{ ESTADO = $estado; DOCUMENTO = $doc; 'NOMBRE SOCIEDAD' = $soc; 'FECHA EVENTO' = $fecha })
            }
        }
    }
    finally {
        if ($tsvWriter) { $tsvWriter.Close() }
        if ($dtuWriter) { $dtuWriter.Close() }
    }

    $files = @()
    if ($EmitXlsx -and $rows.Count -gt 0) {
        $rows | Export-Excel -Path $fileXlsx -WorksheetName 'Usuarios Retirados' -AutoSize -BoldTopRow -FreezeTopRow -ClearSheet
        $files += $fileXlsx
    }
    if ($EmitTsv) { $files += $fileTsv }
    if ($EmitDtu) { $files += $fileDtu }

    return [pscustomobject]@{ Files = $files; Rows = $rows.Count }
}

# ---------------------------------------------------------------------------
#  Exportacion directa (compatibilidad): selecciona y escribe en una llamada
# ---------------------------------------------------------------------------
function Export-UsuariosRetirados {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$OutputDir,
        [string]$EncodingIn = 'Windows-1252',
        [string]$FechaEvento,                       # dd/MM/yyyy ; vacio = sin filtro de fecha
        [string[]]$Sociedades,                      # vacio = todas
        [string[]]$Estados = @('Terminated'),
        [switch]$EmitXlsx,
        [switch]$EmitTsv,
        [switch]$EmitDtu,
        [int]$PreviewLimit = 0,                     # > 0 => solo vista previa (no escribe)
        [int]$ProgressEvery = 20000,
        [scriptblock]$OnProgress
    )

    $isPreview = $PreviewLimit -gt 0
    $max = if ($isPreview) { $PreviewLimit } else { 0 }

    $sel = Select-EmpleadoFilas -Path $Path -EncodingIn $EncodingIn -FechaEvento $FechaEvento `
        -Sociedades $Sociedades -Estados $Estados -MaxFilas $max -ProgressEvery $ProgressEvery -OnProgress $OnProgress

    if ($isPreview) {
        $rows = New-Object 'System.Collections.Generic.List[object]'
        foreach ($f in $sel.Filas) {
            [void]$rows.Add([pscustomobject]@{ ESTADO = $f[0]; DOCUMENTO = $f[1]; 'NOMBRE SOCIEDAD' = $f[2]; 'FECHA EVENTO' = $f[3] })
        }
        return [pscustomobject]@{
            TotalFilas = $sel.TotalFilas; Malformed = $sel.Malformed; Coinciden = $sel.Coinciden
            Preview = $true; Rows = $rows; Files = @(); FechaTag = $sel.FechaTag
        }
    }

    $w = Write-UsuariosRetirados -Filas $sel.Filas -OutputDir $OutputDir -FechaTag $sel.FechaTag `
        -EmitXlsx:$EmitXlsx -EmitTsv:$EmitTsv -EmitDtu:$EmitDtu

    return [pscustomobject]@{
        TotalFilas = $sel.TotalFilas; Malformed = $sel.Malformed; Coinciden = $sel.Coinciden
        Preview = $false; Rows = $w.Rows; Files = $w.Files; FechaTag = $sel.FechaTag
    }
}
