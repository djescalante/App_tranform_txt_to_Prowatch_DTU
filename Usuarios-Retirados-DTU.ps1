#Requires -Version 5.1
<#
    Transforma Empleados.txt (CSV por comas, Windows-1252) y genera:
      1) "Usuarios Retirados <FECHA>.csv"      -> TAB, UTF-8 con BOM, con encabezado
      2) "Usuarios Retirados DTU al <FECHA>.txt" -> DOCUMENTO<TAB>FECHA<TAB>T, UTF-8 con BOM
    Filtro: ESTADO = 'Terminated' Y FECHA EVENTO = fecha indicada (dd/MM/yyyy)
#>
[CmdletBinding()]
param(
    [string]$InputPath   = 'E:\CarpetaTrabajoIA\empleados\Empleados.txt',
    [string]$OutputDir   = 'E:\CarpetaTrabajoIA\empleados',
    [string]$FechaEvento = '28/09/2026',
    [string]$EstadoFiltro = 'Terminated',
    [string]$EncodingIn  = 'Windows-1252'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName Microsoft.VisualBasic
$VBParser = [Microsoft.VisualBasic.FileIO.TextFieldParser]

$colEstado = 'ESTADO'
$colDoc    = 'DOCUMENTO'
$colSoc    = 'NOMBRE SOCIEDAD'
$colFecha  = 'FECHA EVENTO'

# Fecha segura para nombres de archivo (dd-MM-yyyy)
$fechaArchivo = $FechaEvento -replace '/', '-'
$outCsv = Join-Path $OutputDir ("Usuarios Retirados $fechaArchivo.csv")
$outTxt = Join-Path $OutputDir ("Usuarios Retirados DTU al $fechaArchivo.txt")

$encIn  = [System.Text.Encoding]::GetEncoding($EncodingIn)
$encOut = New-Object System.Text.UTF8Encoding($true)   # UTF-8 con BOM

$parser = New-Object $VBParser($InputPath, $encIn)
$parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
$parser.SetDelimiters(',')
$parser.HasFieldsEnclosedInQuotes = $true
$parser.TrimWhiteSpace = $false

$csvWriter = New-Object System.IO.StreamWriter($outCsv, $false, $encOut)
$txtWriter = New-Object System.IO.StreamWriter($outTxt, $false, $encOut)

$totalLeidas = 0
$totalSalida = 0
$lineasMalas = 0

try {
    # Encabezado -> localizar columnas por NOMBRE
    $header = $parser.ReadFields()
    $idx = @{}
    for ($i = 0; $i -lt $header.Count; $i++) {
        $name = $header[$i].Trim()
        if (-not $idx.ContainsKey($name)) { $idx[$name] = $i }
    }

    foreach ($required in @($colEstado, $colDoc, $colSoc, $colFecha)) {
        if (-not $idx.ContainsKey($required)) {
            throw "No se encontró la columna '$required' en el encabezado."
        }
    }

    $iEstado = $idx[$colEstado]
    $iDoc    = $idx[$colDoc]
    $iSoc    = $idx[$colSoc]
    $iFecha  = $idx[$colFecha]

    # Salida CSV con encabezado
    $csvWriter.WriteLine(($colEstado, $colDoc, $colSoc, $colFecha) -join "`t")

    while (-not $parser.EndOfData) {
        try {
            $f = $parser.ReadFields()
        }
        catch [Microsoft.VisualBasic.FileIO.MalformedLineException] {
            $lineasMalas++
            Write-Warning "Línea mal formada omitida (nro $($_.Exception.LineNumber)): $($_.Exception.Message)"
            continue
        }

        $totalLeidas++

        if ($f.Count -le $iFecha) { continue }

        $estado = $f[$iEstado].Trim()
        $fecha  = $f[$iFecha].Trim()

        if ($estado -ne $EstadoFiltro) { continue }
        if ($fecha  -ne $FechaEvento)  { continue }

        $doc = $f[$iDoc].Trim()
        $soc = $f[$iSoc].Trim()

        $csvWriter.WriteLine(($estado, $doc, $soc, $fecha) -join "`t")
        $txtWriter.WriteLine(($doc, $fecha, 'T') -join "`t")
        $totalSalida++
    }
}
finally {
    $parser.Close()
    $csvWriter.Close()
    $txtWriter.Close()
}

Write-Host ("Filas leídas       : {0:N0}" -f $totalLeidas)
Write-Host ("Filas mal formadas : {0:N0}" -f $lineasMalas)
Write-Host ("Filas de salida    : {0:N0}" -f $totalSalida)
Write-Host ("CSV : {0}" -f $outCsv)
Write-Host ("TXT : {0}" -f $outTxt)
