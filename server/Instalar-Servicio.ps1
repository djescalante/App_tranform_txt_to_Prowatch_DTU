#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Instala o desinstala PW Extended App como un Servicio de Windows (desde el repo).

.DESCRIPTION
    Compila la aplicacion (framework-dependent) en server\publish y registra el
    servicio "PWExtendedApp" con inicio automatico. Si existe el servicio anterior
    "UsuariosRetiradosDTU", lo detiene y lo elimina.

    Datos:
      - Usuarios, historial y Lista VIP: <repo>\data\usuarios_retirados.db. Si el
        servicio anterior la habia dejado en C:\Windows\System32 (error de versiones
        previas), se copia aqui antes de arrancar.
      - Ocupacion Edificios: prowatch.db (parametro -OcupacionDbPath).

    El secreto JWT se reutiliza del servicio existente (asi no se cierran las
    sesiones); si no hay uno, se genera y se muestra al final: guardelo.

    Compatible con Windows PowerShell 5.1.

.PARAMETER Action
    'Install' para crear el servicio, 'Uninstall' para removerlo o 'Status'.
#>
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'Status')]
    [string]$Action = 'Install',
    [string]$ServiceName = 'PWExtendedApp',
    [string]$DisplayName = 'PW Extended App - Servicio Web',
    [int]$Port = 443,
    [string]$JwtSecret,
    [string]$CertPath,
    [string]$CertPassword,
    [string]$OcupacionDbPath
)

$ErrorActionPreference = 'Stop'
$LegacyServiceName = 'UsuariosRetiradosDTU'
$serverDir = $PSScriptRoot
$repoDir = Split-Path $serverDir -Parent
$publishDir = Join-Path $serverDir "publish"
# Bases fuera del codigo fuente: server\Data es codigo y Windows no distingue mayusculas.
$dataDir = Join-Path $repoDir "data"

function Get-ServiceEnvValue([string]$service, [string]$name) {
    $props = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$service" -Name Environment -ErrorAction SilentlyContinue
    if (-not $props) { return $null }
    foreach ($line in $props.Environment) {
        if ($line.StartsWith("$name=")) { return $line.Substring($name.Length + 1) }
    }
    return $null
}

function Remove-AppService([string]$name) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if (-not $svc) { return $false }
    Write-Host "    Deteniendo y eliminando el servicio '$name'..."
    Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
    sc.exe delete $name | Out-Null
    Start-Sleep -Seconds 2
    return $true
}

if ($Action -eq 'Uninstall') {
    Write-Host "==> Desinstalando..." -ForegroundColor Yellow
    $removed = (Remove-AppService $ServiceName)
    if (Remove-AppService $LegacyServiceName) { $removed = $true }
    if ($removed) { Write-Host "Servicio desinstalado. Se conservan <repo>\data y los certificados." -ForegroundColor Green }
    else { Write-Host "No habia servicios instalados." -ForegroundColor Yellow }
    exit 0
}

if ($Action -eq 'Status') {
    foreach ($name in @($ServiceName, $LegacyServiceName)) {
        $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($svc) { Write-Host "Servicio '$name': $($svc.Status)" -ForegroundColor Cyan }
        else { Write-Host "El servicio '$name' no esta instalado." -ForegroundColor DarkGray }
    }
    exit 0
}

# ---------------- Install ----------------

# 1. Secreto JWT: se reutiliza el del servicio actual o el anterior (no cierra sesiones).
if (-not $JwtSecret) { $JwtSecret = Get-ServiceEnvValue $ServiceName 'Jwt__Secret' }
if (-not $JwtSecret) { $JwtSecret = Get-ServiceEnvValue $LegacyServiceName 'Jwt__Secret' }
$jwtGenerated = $false
if (-not $JwtSecret) {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $JwtSecret = [Convert]::ToBase64String($bytes)
    $jwtGenerated = $true
}

# 2. Detener servicios (el anterior corre desde server\publish y bloquearia la compilacion).
Write-Host "==> Deteniendo servicios existentes..." -ForegroundColor Cyan
Remove-AppService $ServiceName | Out-Null
if (Remove-AppService $LegacyServiceName) {
    Write-Host "    Servicio anterior '$LegacyServiceName' eliminado." -ForegroundColor Yellow
}

# 3. Compilar y publicar
Write-Host "==> Compilando y publicando aplicacion..." -ForegroundColor Cyan
$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnetExe = $cmd.Source }
}
& $dotnetExe publish (Join-Path $serverDir "UsuariosRetirados.Server.csproj") `
    -c Release -o $publishDir --self-contained false --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) {
    Write-Error "Fallo la publicacion de la aplicacion."
    exit 1
}
$exePath = Join-Path $publishDir "UsuariosRetirados.Server.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "No se encontro el ejecutable en '$exePath'."
    exit 1
}

# 4. Certificado TLS (PFX). Por defecto server\certs\server.pfx + pfx-password.txt.
if (-not $CertPath) { $CertPath = Join-Path $serverDir "certs\server.pfx" }
if (-not (Test-Path $CertPath)) {
    Write-Error "No se encontro el certificado PFX en '$CertPath'. Genere uno (ver server\README.md) o pase -CertPath."
    exit 1
}
if (-not $CertPassword) {
    $pwdFile = Join-Path $serverDir "certs\pfx-password.txt"
    if (Test-Path $pwdFile) { $CertPassword = (Get-Content -LiteralPath $pwdFile -Raw).Trim() }
}
if (-not $CertPassword) {
    Write-Error "Falta -CertPassword (o el archivo certs\pfx-password.txt)."
    exit 1
}
$publishCertsDir = Join-Path $publishDir "certs"
if (-not (Test-Path $publishCertsDir)) { New-Item -ItemType Directory -Path $publishCertsDir -Force | Out-Null }
$serviceCertPath = Join-Path $publishCertsDir "server.pfx"
Copy-Item -LiteralPath $CertPath -Destination $serviceCertPath -Force

# 5. Bases de datos
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir -Force | Out-Null }
$usersDb = Join-Path $dataDir "usuarios_retirados.db"
$legacyDb = Join-Path $env:WINDIR "System32\usuarios_retirados.db"
if (-not (Test-Path $usersDb) -and (Test-Path $legacyDb)) {
    Write-Host "==> Migrando usuarios, historial y Lista VIP desde $legacyDb ..." -ForegroundColor Cyan
    foreach ($suffix in @('', '-wal', '-shm')) {
        if (Test-Path ($legacyDb + $suffix)) { Copy-Item -LiteralPath ($legacyDb + $suffix) -Destination ($usersDb + $suffix) }
    }
}

if (-not $OcupacionDbPath) { $OcupacionDbPath = Join-Path $dataDir "prowatch.db" }
if (Test-Path $OcupacionDbPath) {
    Write-Host "==> Ocupacion Edificios: $OcupacionDbPath" -ForegroundColor Cyan
} else {
    Write-Warning "No se encontro prowatch.db en '$OcupacionDbPath'. Se creara vacia; copie la base ahi o use -OcupacionDbPath."
}

# 6. El puerto debe estar libre (p. ej. no debe correr Iniciar-Servidor.cmd).
$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    Write-Error "El puerto $Port ya esta en uso por '$($owner.ProcessName)' (PID $($listener.OwningProcess)). Detengalo o use -Port <otro>."
    exit 1
}

# 7. Registrar el servicio
Write-Host "==> Registrando Servicio de Windows '$ServiceName'..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName $DisplayName `
    -Description "PW Extended App: Usuarios Retirados DTU, Ocupacion Edificios y herramientas de Control de Acceso para ProWatch" `
    -StartupType Automatic | Out-Null

$serviceRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $serviceRegPath -Name Environment -PropertyType MultiString -Force -Value @(
    "Jwt__Secret=$JwtSecret",
    "ASPNETCORE_ENVIRONMENT=Production",
    "Kestrel__Endpoints__Https__Url=https://0.0.0.0:$Port",
    "Kestrel__Endpoints__Https__Certificate__Path=$serviceCertPath",
    "Kestrel__Endpoints__Https__Certificate__Password=$CertPassword",
    "ConnectionStrings__DefaultConnection=Data Source=$usersDb",
    "Ocupacion__DbPath=$OcupacionDbPath"
) | Out-Null

sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

$ruleName = "PW Extended App $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

# 8. Arranque
Write-Host "==> Iniciando servicio..." -ForegroundColor Cyan
Start-Service -Name $ServiceName
Start-Sleep -Seconds 5
$svc = Get-Service -Name $ServiceName
if ($svc.Status -ne 'Running') {
    Write-Error "El servicio quedo en estado '$($svc.Status)'. Revise el Visor de eventos (Aplicacion, origen .NET Runtime)."
    exit 1
}

$urlPort = if ($Port -eq 443) { "" } else { ":$Port" }
Write-Host "==> Servicio en ejecucion: https://localhost$urlPort" -ForegroundColor Green
Write-Host "    (el dashboard de Ocupacion Edificios se precalcula en segundo plano ~20 s)"
if ($jwtGenerated) {
    Write-Host ""
    Write-Host "==> Se genero un secreto JWT nuevo (guardelo):" -ForegroundColor Yellow
    Write-Host "    $JwtSecret"
}
