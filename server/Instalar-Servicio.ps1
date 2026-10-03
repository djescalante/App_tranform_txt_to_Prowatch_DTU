#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Instala o desinstala Usuarios Retirados DTU como un Servicio de Windows.

.DESCRIPTION
    Publica la aplicación en modo autocontenido (o framework-dependent) y registra el servicio
    con inicio automático para que corra en segundo plano de manera continua.

.PARAMETER Action
    'Install' para crear el servicio o 'Uninstall' para detenerlo y removerlo.
#>
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Status')]
    [string]$Action = 'Install',
    [string]$ServiceName = 'UsuariosRetiradosDTU',
    [string]$DisplayName = 'Usuarios Retirados DTU - Servicio Web',
    [int]$Port = 5000
)

$serverDir = $PSScriptRoot
$publishDir = Join-Path $serverDir "publish"

if ($Action -eq 'Install') {
    Write-Host "==> Compilando y publicando aplicacion..." -ForegroundColor Cyan
    $dotnetExe = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
    if (-not $dotnetExe) { $dotnetExe = "C:\Program Files\dotnet\dotnet.exe" }

    & $dotnetExe publish (Join-Path $serverDir "UsuariosRetirados.Server.csproj") `
        -c Release -o $publishDir --self-contained false

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Fallo la publicacion de la aplicacion."
        exit 1
    }

    $exePath = Join-Path $publishDir "UsuariosRetirados.Server.exe"
    if (-not (Test-Path $exePath)) {
        Write-Error "No se encontro el ejecutable en '$exePath'."
        exit 1
    }

    Write-Host "==> Registrando Servicio de Windows '$ServiceName'..." -ForegroundColor Cyan
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "El servicio ya existe. Deteniendo..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    New-Service -Name $ServiceName `
        -BinaryPathName "`"$exePath`"" `
        -DisplayName $DisplayName `
        -Description "Servidor Web Cliente-Servidor para procesamiento del padrón de empleados y generación de DTU ProWatch" `
        -StartupType Automatic

    # Recovery actions
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

    Write-Host "==> Iniciando servicio..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2

    $svc = Get-Service -Name $ServiceName
    Write-Host "==> Estado del servicio: $($svc.Status)" -ForegroundColor Green
    Write-Host "==> Aplicacion disponible en: http://localhost:$Port" -ForegroundColor Green
}
elseif ($Action -eq 'Uninstall') {
    Write-Host "==> Deteniendo y removiendo servicio '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Write-Host "Servicio desinstalado exitosamente." -ForegroundColor Green
}
elseif ($Action -eq 'Status') {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Host "Servicio '$ServiceName': $($svc.Status)" -ForegroundColor Cyan
    } else {
        Write-Host "El servicio '$ServiceName' no esta instalado." -ForegroundColor Yellow
    }
}
