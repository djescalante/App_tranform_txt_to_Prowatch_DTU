#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Instala o desinstala Usuarios Retirados DTU como un Servicio de Windows.

.DESCRIPTION
    Publica la aplicacion (framework-dependent) y registra el servicio con inicio
    automatico para que corra en segundo plano de manera continua.

    El secreto JWT requerido por la aplicacion se configura como variable de
    entorno del servicio (Jwt__Secret) en el registro. Si no se pasa -JwtSecret,
    se genera uno aleatorio y se informa al final de la instalacion: guardelo,
    porque sera necesario para futuras reinstalaciones.

.PARAMETER Action
    'Install' para crear el servicio, 'Uninstall' para removerlo o 'Status'.
#>
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Status')]
    [string]$Action = 'Install',
    [string]$ServiceName = 'UsuariosRetiradosDTU',
    [string]$DisplayName = 'Usuarios Retirados DTU - Servicio Web',
    [int]$Port = 443,
    [string]$JwtSecret,
    [string]$CertPath,
    [string]$CertPassword
)

$serverDir = $PSScriptRoot
$publishDir = Join-Path $serverDir "publish"

if ($Action -eq 'Install') {
    Write-Host "==> Compilando y publicando aplicacion..." -ForegroundColor Cyan
    $dotnetExe = $null
    if (Test-Path "C:\Program Files\dotnet\dotnet.exe") {
        $dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
    }
    else {
        $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($cmd) { $dotnetExe = $cmd.Source }
    }
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

    if (-not $JwtSecret) {
        $bytes = New-Object byte[] 48
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $JwtSecret = [Convert]::ToBase64String($bytes)
        Write-Host "==> Se genero un secreto JWT aleatorio para el servicio." -ForegroundColor Yellow
    }

    # Certificado TLS (PFX). Si no se pasa, se usan server\certs\server.pfx y
    # server\certs\pfx-password.txt (generados con New-SelfSignedCertificate).
    if (-not $CertPath) {
        $CertPath = Join-Path $serverDir "certs\server.pfx"
    }
    if (-not (Test-Path $CertPath)) {
        Write-Error "No se encontro el certificado PFX en '$CertPath'. Genere uno (ver server\README.md) o pase -CertPath."
        exit 1
    }
    if (-not $CertPassword) {
        $pwdFile = Join-Path $serverDir "certs\pfx-password.txt"
        if (Test-Path $pwdFile) {
            $CertPassword = (Get-Content -LiteralPath $pwdFile -Raw).Trim()
        }
    }
    if (-not $CertPassword) {
        Write-Error "Falta -CertPassword (o el archivo certs\pfx-password.txt)."
        exit 1
    }

    # El servicio usa una copia del PFX dentro de publish para ser autocontenido.
    $publishCertsDir = Join-Path $publishDir "certs"
    if (-not (Test-Path $publishCertsDir)) { New-Item -ItemType Directory -Path $publishCertsDir -Force | Out-Null }
    $serviceCertPath = Join-Path $publishCertsDir "server.pfx"
    Copy-Item -LiteralPath $CertPath -Destination $serviceCertPath -Force

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

    # Variables de entorno del servicio (leidas por el host al iniciar).
    $serviceRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    New-ItemProperty -Path $serviceRegPath -Name Environment -PropertyType MultiString -Force -Value @(
        "Jwt__Secret=$JwtSecret",
        "ASPNETCORE_ENVIRONMENT=Production",
        "Kestrel__Endpoints__Https__Url=https://0.0.0.0:$Port",
        "Kestrel__Endpoints__Https__Certificate__Path=$serviceCertPath",
        "Kestrel__Endpoints__Https__Certificate__Password=$CertPassword"
    ) | Out-Null

    # Recovery actions
    sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

    Write-Host "==> Iniciando servicio..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2

    $svc = Get-Service -Name $ServiceName
    Write-Host "==> Estado del servicio: $($svc.Status)" -ForegroundColor Green
    Write-Host "==> Aplicacion disponible en: https://localhost:$Port" -ForegroundColor Green
    Write-Host ""
    Write-Host "==> Secreto JWT del servicio (guardelo):" -ForegroundColor Yellow
    Write-Host "    $JwtSecret"
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
