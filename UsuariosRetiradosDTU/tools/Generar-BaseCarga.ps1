#Requires -Version 5.1
<#
.SYNOPSIS
    Genera un padron reducido Empleados_base_carga_<dd-MM-yyyy>.txt con los
    ultimos N dias de eventos (por defecto 5), a partir de Empleados.txt.

.DESCRIPTION
    Lee el padron Empleados.txt (CSV por comas, encabezado, Windows-1252),
    calcula la fecha de evento mas reciente (maximo global del archivo) y
    escribe un archivo mas liviano con los registros de los ultimos N dias
    calendario contados hacia atras desde esa fecha maxima.

    La salida conserva TODAS las columnas y el encabezado original, se escribe
    en Windows-1252 sin BOM y queda ordenada por FECHA EVENTO asc y luego
    DOCUMENTO asc. La escritura es atomica (archivo .tmp -> Move-Item), de modo
    que la aplicacion nunca lee un archivo a medio escribir.

    Pensado para ejecutarse como tarea programada en el servidor (cuenta SYSTEM)
    despues de que se genere el archivo de entrada. Es autocontenido: no depende
    de Motor.ps1 ni de modulos externos.

.PARAMETER InputPath
    Ruta del padron de entrada.
    Default: \\Sbcldwppws01\d$\INTEGRACION_AD\INTEGRACION_AD\Empleados.txt

.PARAMETER OutputDir
    Carpeta de salida. Default: la carpeta donde esta el script ($PSScriptRoot).

.PARAMETER Dias
    Cantidad de dias calendario inclusivos hacia atras desde el maximo. Default 5.
    Ejemplo: si la fecha mas reciente es 30/09, la ventana es 26/09..30/09.

.PARAMETER EncodingIn
    Codificacion del archivo de entrada y de la salida. Default Windows-1252.

.PARAMETER LogDir
    Carpeta de logs. Default: <carpeta del script>\logs

.PARAMETER RetenerDias
    Dias de retencion para las bases y los logs generados. Default 15.

.PARAMETER StableCopy
    Si se indica, ademas de la copia versionada deja una copia fija llamada
    Empleados_base_carga.txt, util para apuntar siempre la app al mismo nombre.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Generar-BaseCarga.ps1

.EXAMPLE
    .\Generar-BaseCarga.ps1 -Dias 7 -StableCopy

.NOTES
    Tarea programada (ejemplo, correr en el servidor):
    schtasks /Create /TN "UsuariosRetiradosDTU - Base Carga" /SC DAILY /ST 09:00 /RU SYSTEM /RL HIGHEST /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"\\Sbcldwppws01\d$\INTEGRACION_AD\Script_Limpieza\Generar-BaseCarga.ps1\""

    Salida (codigo): 0 = OK, 1 = error. El detalle queda en el log.
#>
[CmdletBinding()]
param(
    [string]$InputPath  = '\\Sbcldwppws01\d$\INTEGRACION_AD\INTEGRACION_AD\Empleados.txt',
    [string]$OutputDir  = '',
    [int]$Dias          = 5,
    [string]$EncodingIn = 'Windows-1252',
    [string]$LogDir     = '',
    [int]$RetenerDias   = 15,
    [switch]$StableCopy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName Microsoft.VisualBasic

$colEstado = 'ESTADO'
$colDoc    = 'DOCUMENTO'
$colSoc    = 'NOMBRE SOCIEDAD'
$colFecha  = 'FECHA EVENTO'

$script:LogFile = $null
$parser = $null
$fs     = $null

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Message
    Write-Host $line
    if ($script:LogFile) {
        try { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 } catch { }
    }
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

function ConvertTo-CsvLine {
    param([string[]]$Campos)
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $Campos.Count; $i++) {
        if ($i -gt 0) { [void]$sb.Append(',') }
        $v = [string]$Campos[$i]
        if ($v -match '[",\r\n]') {
            $v = '"' + ($v -replace '"', '""') + '"'
        }
        [void]$sb.Append($v)
    }
    return $sb.ToString()
}

$startTime = Get-Date

try {
    if (-not $OutputDir) { $OutputDir = $PSScriptRoot }
    if (-not $LogDir)    { $LogDir    = Join-Path $PSScriptRoot 'logs' }

    if (-not (Test-Path -LiteralPath $LogDir))    { New-Item -ItemType Directory -Path $LogDir    -Force | Out-Null }
    if (-not (Test-Path -LiteralPath $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
    $script:LogFile = Join-Path $LogDir ('Generar-BaseCarga_{0}.log' -f (Get-Date).ToString('yyyyMMdd_HHmmss'))

    Write-Log "Inicio. Origen='$InputPath'"
    Write-Log "Parametros: Dias=$Dias, EncodingIn=$EncodingIn, OutputDir='$OutputDir', RetenerDias=$RetenerDias, StableCopy=$StableCopy"

    if ($Dias -lt 1) { throw "El parametro -Dias debe ser >= 1." }
    if (-not (Test-Path -LiteralPath $InputPath)) { throw "No existe el archivo de entrada: $InputPath" }

    $encIn = [System.Text.Encoding]::GetEncoding($EncodingIn)

    # FileShare.ReadWrite para tolerar que el generador aun tenga el archivo abierto.
    $fs = New-Object System.IO.FileStream($InputPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($fs, $encIn)
    $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
    $parser.SetDelimiters(',')
    $parser.HasFieldsEnclosedInQuotes = $true
    $parser.TrimWhiteSpace = $false

    if ($parser.EndOfData) { throw 'El archivo de entrada esta vacio.' }
    $header = $parser.ReadFields()
    if (-not $header -or $header.Count -eq 0) { throw 'No se pudo leer el encabezado.' }

    $idx = @{}
    for ($i = 0; $i -lt $header.Count; $i++) {
        $name = ([string]$header[$i]).Trim()
        if (-not $idx.ContainsKey($name)) { $idx[$name] = $i }
    }
    foreach ($req in @($colEstado, $colDoc, $colSoc, $colFecha)) {
        if (-not $idx.ContainsKey($req)) { throw "No se encontro la columna '$req' en el encabezado." }
    }
    $iFecha = $idx[$colFecha]
    $iDoc   = $idx[$colDoc]
    $headerLine = ConvertTo-CsvLine -Campos $header
    Write-Log ("Encabezado OK: {0} columnas. FECHA EVENTO en indice {1}, DOCUMENTO en {2}." -f $header.Count, $iFecha, $iDoc)

    # ------------------------------------------------------------------
    #  Un solo barrido con maximo corredizo. Solo se conservan en memoria
    #  las filas que caen dentro de la ventana [max-(Dias-1) .. max].
    # ------------------------------------------------------------------
    $buf = New-Object 'System.Collections.Generic.List[object]'
    $total = 0; $mal = 0
    $maxDate = $null; $floor = $null

    while (-not $parser.EndOfData) {
        try { $f = $parser.ReadFields() }
        catch [Microsoft.VisualBasic.FileIO.MalformedLineException] { $mal++; continue }

        $total++
        if ($f.Count -le $iFecha) { continue }

        $dt = ConvertTo-FechaEvento $f[$iFecha]
        if (-not $dt) { continue }

        if ((-not $maxDate) -or ($dt -gt $maxDate)) {
            $maxDate = $dt
            $floor = $dt.AddDays(-1 * ($Dias - 1))
            for ($j = $buf.Count - 1; $j -ge 0; $j--) {
                if ($buf[$j].FechaKey -lt $floor) { $buf.RemoveAt($j) }
            }
        }

        if ($dt -lt $floor) { continue }

        $buf.Add([pscustomobject]@{
            FechaKey = $dt
            Doc      = ([string]$f[$iDoc]).Trim()
            Campos   = $f
        })
    }

    if ($maxDate) {
        Write-Log ("Fecha maxima detectada: {0:dd/MM/yyyy}" -f $maxDate)
        Write-Log ("Ventana ({0} dias): {1:dd/MM/yyyy} a {2:dd/MM/yyyy}" -f $Dias, $floor, $maxDate)
    }
    else {
        Write-Log 'ADVERTENCIA: no se detectaron fechas validas; la salida tendra solo el encabezado.'
    }

    $rows = @($buf | Sort-Object FechaKey, Doc)

    $fechaTag = (Get-Date).ToString('dd-MM-yyyy')
    $outName  = "Empleados_base_carga_$fechaTag.txt"
    $outPath  = Join-Path $OutputDir $outName
    $tmpPath  = "$outPath.tmp"

    $encOut = [System.Text.Encoding]::GetEncoding($EncodingIn)
    $sw = New-Object System.IO.StreamWriter($tmpPath, $false, $encOut)
    try {
        $sw.WriteLine($headerLine)
        foreach ($r in $rows) { $sw.WriteLine((ConvertTo-CsvLine -Campos $r.Campos)) }
    }
    finally {
        $sw.Close()
    }

    Move-Item -LiteralPath $tmpPath -Destination $outPath -Force

    if ($StableCopy) {
        $stablePath = Join-Path $OutputDir 'Empleados_base_carga.txt'
        $stableTmp  = "$stablePath.tmp"
        Copy-Item -LiteralPath $outPath -Destination $stableTmp -Force
        Move-Item -LiteralPath $stableTmp -Destination $stablePath -Force
        Write-Log "Copia estable: $stablePath"
    }

    # ------------------------------------------------------------------
    #  Retencion: elimina bases y logs mas antiguos que -RetenerDias.
    # ------------------------------------------------------------------
    $limite = (Get-Date).AddDays(-1 * $RetenerDias)
    Get-ChildItem -LiteralPath $OutputDir -Filter 'Empleados_base_carga_*.txt' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limite } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue; Write-Log "Purga base antigua: $($_.Name)" }
    Get-ChildItem -LiteralPath $LogDir -Filter 'Generar-BaseCarga_*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limite } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

    Write-Log ("Filas leidas: {0:N0}; mal formadas: {1}; en ventana: {2:N0}." -f $total, $mal, $rows.Count)
    Write-Log "Salida: $outPath"
    Write-Log ("Fin OK. Duracion: {0}" -f ((Get-Date) - $startTime).ToString('hh\:mm\:ss'))
    exit 0
}
catch {
    $msg = "ERROR: $($_.Exception.Message)"
    try { Write-Log $msg } catch { Write-Host $msg }
    try { Write-Log ("Detalle: {0}" -f $_.ScriptStackTrace) } catch { }
    try { Write-Log ("Duracion hasta el error: {0}" -f ((Get-Date) - $startTime).ToString('hh\:mm\:ss')) } catch { }
    exit 1
}
finally {
    if ($parser) { try { $parser.Close() } catch { } }
    elseif ($fs) { try { $fs.Dispose() } catch { } }
}
