#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Instala Usuarios Retirados DTU (app ya compilada) como Servicio de Windows.

.DESCRIPTION
    Se ejecuta desde la raiz del paquete generado por server\Crear-Paquete.ps1:

        <paquete>\
          app\            aplicacion publicada (UsuariosRetirados.Server.exe)
          prerequisitos\  instalador del ASP.NET Core Runtime 10 (x64)
          certs\          PFX + password (se generan aqui si no existen)
          data\           base SQLite (se crea al primer arranque)
          salidas\        archivos generados (DTU / XLSX / TSV)
          Empleados.txt   padron (copiarlo aqui o cambiar la ruta en Administracion)

    No requiere el SDK de .NET ni acceso a internet: solo el ASP.NET Core Runtime,
    que se instala desde prerequisitos\ si falta.

    Compatible con Windows PowerShell 5.1.

.PARAMETER Action
    'Install' (por defecto), 'Uninstall' o 'Status'.
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
    [string]$CertPassword,
    [string]$InputPath,
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$appDir = Join-Path $root 'app'
$certsDir = Join-Path $root 'certs'
$dataDir = Join-Path $root 'data'
$exePath = Join-Path $appDir 'UsuariosRetirados.Server.exe'
$serviceRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

function Get-ServiceEnvValue([string]$name) {
    $props = Get-ItemProperty -Path $serviceRegPath -Name Environment -ErrorAction SilentlyContinue
    if (-not $props) { return $null }
    foreach ($line in $props.Environment) {
        if ($line.StartsWith("$name=")) { return $line.Substring($name.Length + 1) }
    }
    return $null
}

function Test-AspNetRuntime {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { return $false }
    $runtimes = & $dotnet --list-runtimes 2>$null
    return [bool]($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' })
}

if ($Action -eq 'Uninstall') {
    Write-Host "==> Deteniendo y removiendo servicio '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Servicio desinstalado. Se conservan data\, certs\ y salidas\." -ForegroundColor Green
    exit 0
}

if ($Action -eq 'Status') {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) { Write-Host "Servicio '$ServiceName': $($svc.Status)" -ForegroundColor Cyan }
    else { Write-Host "El servicio '$ServiceName' no esta instalado." -ForegroundColor Yellow }
    exit 0
}

if (-not (Test-Path $exePath)) {
    Write-Error "No se encontro '$exePath'. Ejecute este script desde la raiz del paquete."
    exit 1
}

# 1. ASP.NET Core Runtime 10 (x64)
Write-Host "==> Verificando ASP.NET Core Runtime 10..." -ForegroundColor Cyan
if (-not (Test-AspNetRuntime)) {
    $installer = Get-ChildItem -Path (Join-Path $root 'prerequisitos') -Filter 'aspnetcore-runtime-10*-win-x64.exe' -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $installer) {
        Write-Error "Falta el ASP.NET Core Runtime 10 y no se encontro su instalador en prerequisitos\."
        exit 1
    }
    Write-Host "    Instalando $($installer.Name) (silencioso)..."
    $p = Start-Process -FilePath $installer.FullName -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Write-Error "El instalador del runtime termino con codigo $($p.ExitCode)."
        exit 1
    }
    if (-not (Test-AspNetRuntime)) {
        Write-Error "El runtime no aparece instalado tras ejecutar el instalador."
        exit 1
    }
}
Write-Host "    OK"

# 2. Certificado TLS (PFX). Si no existe, se genera uno autofirmado con el
#    nombre del servidor para que los clientes de la LAN no vean error de nombre.
if (-not (Test-Path $certsDir)) { New-Item -ItemType Directory -Path $certsDir -Force | Out-Null }
if (-not $CertPath) { $CertPath = Join-Path $certsDir 'server.pfx' }
$pwdFile = Join-Path $certsDir 'pfx-password.txt'

if (-not (Test-Path $CertPath)) {
    Write-Host "==> Generando certificado autofirmado..." -ForegroundColor Cyan
    $dnsNames = @($env:COMPUTERNAME)
    try {
        $fqdn = [System.Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName
        if ($fqdn -and $dnsNames -notcontains $fqdn) { $dnsNames += $fqdn }
    } catch { }
    $dnsNames += 'localhost'
    Write-Host "    Nombres: $($dnsNames -join ', ')"

    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $CertPassword = -join (1..28 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
    $sec = ConvertTo-SecureString $CertPassword -AsPlainText -Force
    $cert = New-SelfSignedCertificate -DnsName $dnsNames -CertStoreLocation Cert:\LocalMachine\My `
        -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(2) -KeyExportPolicy Exportable
    Export-PfxCertificate -Cert $cert -FilePath $CertPath -Password $sec | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $certsDir 'server.cer') | Out-Null
    [System.IO.File]::WriteAllText($pwdFile, $CertPassword)

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('My', 'LocalMachine')
    $store.Open('ReadWrite')
    $store.Remove($cert)
    $store.Close()
}
if (-not $CertPassword -and (Test-Path $pwdFile)) {
    $CertPassword = (Get-Content -LiteralPath $pwdFile -Raw).Trim()
}
if (-not $CertPassword) {
    Write-Error "Falta -CertPassword (o el archivo certs\pfx-password.txt)."
    exit 1
}

# 3. Secreto JWT: se reutiliza el del servicio existente para no invalidar sesiones.
$jwtFile = Join-Path $certsDir 'jwt-secret.txt'
if (-not $JwtSecret) { $JwtSecret = Get-ServiceEnvValue 'Jwt__Secret' }
if (-not $JwtSecret -and (Test-Path $jwtFile)) { $JwtSecret = (Get-Content -LiteralPath $jwtFile -Raw).Trim() }
if (-not $JwtSecret) {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $JwtSecret = [Convert]::ToBase64String($bytes)
    Write-Host "==> Se genero un secreto JWT nuevo." -ForegroundColor Yellow
}
[System.IO.File]::WriteAllText($jwtFile, $JwtSecret)

# certs\ contiene secretos: solo Administradores y SYSTEM.
icacls $certsDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null

# 4. Rutas de datos
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir -Force | Out-Null }
if (-not $OutputDir) { $OutputDir = Join-Path $root 'salidas' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
if (-not $InputPath) { $InputPath = Join-Path $root 'Empleados.txt' }
if (-not (Test-Path $InputPath)) {
    Write-Warning "No se encontro el padron en '$InputPath'. Copielo ahi o cambie la ruta en Administracion."
}

# 5. TLS 1.3 requiere Windows Server 2022 / Windows 11 (build 20348+).
$build = [Environment]::OSVersion.Version.Build
$tlsEnv = @()
if ($build -lt 20348) {
    Write-Warning "Windows build $build no soporta TLS 1.3: se habilita TLS 1.2 + 1.3."
    $tlsEnv = @(
        'Kestrel__Endpoints__Https__SslProtocols__0=Tls12',
        'Kestrel__Endpoints__Https__SslProtocols__1=Tls13'
    )
}

# 6. Servicio
Write-Host "==> Registrando Servicio de Windows '$ServiceName'..." -ForegroundColor Cyan
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "    El servicio ya existe. Deteniendo y reemplazando..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# El puerto debe estar libre (p. ej. no debe correr Iniciar-Servidor.cmd).
$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    Write-Error "El puerto $Port ya esta en uso por '$($owner.ProcessName)' (PID $($listener.OwningProcess)). Detengalo o use -Port <otro>."
    exit 1
}

New-Service -Name $ServiceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName $DisplayName `
    -Description 'Servidor web para procesamiento del padron de empleados y generacion de DTU ProWatch' `
    -StartupType Automatic | Out-Null

# Nota: Kestrel lee el PFX de Certificate:Path / Certificate:Password.
# AppPaths__* solo siembran la configuracion inicial; luego se editan en Administracion.
$envVars = @(
    "Jwt__Secret=$JwtSecret",
    'ASPNETCORE_ENVIRONMENT=Production',
    "Kestrel__Endpoints__Https__Url=https://0.0.0.0:$Port",
    "Kestrel__Endpoints__Https__Certificate__Path=$CertPath",
    "Kestrel__Endpoints__Https__Certificate__Password=$CertPassword",
    "ConnectionStrings__DefaultConnection=Data Source=$(Join-Path $dataDir 'usuarios_retirados.db')",
    "AppPaths__InputPath=$InputPath",
    "AppPaths__OutputDir=$OutputDir"
) + $tlsEnv
New-ItemProperty -Path $serviceRegPath -Name Environment -PropertyType MultiString -Force -Value $envVars | Out-Null

sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# 7. Firewall
$ruleName = "UsuariosRetiradosDTU $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    Write-Host "==> Abriendo puerto $Port en el firewall..." -ForegroundColor Cyan
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

# 8. Arranque y verificacion
Write-Host "==> Iniciando servicio..." -ForegroundColor Cyan
Start-Service -Name $ServiceName
Start-Sleep -Seconds 5
$svc = Get-Service -Name $ServiceName
if ($svc.Status -ne 'Running') {
    Write-Error "El servicio quedo en estado '$($svc.Status)'. Revise el Visor de eventos (Aplicacion, origen .NET Runtime / UsuariosRetiradosDTU)."
    exit 1
}

Write-Host ""
Write-Host "==> Servicio en ejecucion." -ForegroundColor Green
$urlPort = if ($Port -eq 443) { "" } else { ":$Port" }
Write-Host "    URL:  https://$($env:COMPUTERNAME)$urlPort" -ForegroundColor Green
Write-Host "    Login inicial: admin / Admin123!  (cambiarla de inmediato)" -ForegroundColor Yellow
Write-Host "    Secreto JWT guardado en certs\jwt-secret.txt (necesario para reinstalar)."
Write-Host "    Para evitar la advertencia del navegador, importe certs\server.cer en"
Write-Host "    'Entidades de certificacion raiz de confianza' de cada equipo cliente."
