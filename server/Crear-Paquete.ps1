<#
.SYNOPSIS
    Genera el paquete de instalacion (app compilada + instalador + runtime) en
    <repo>\paquete\UsuariosRetiradosDTU_<fecha>.zip.

.DESCRIPTION
    En el servidor solo hace falta descomprimir y ejecutar Instalar.cmd como
    administrador (ver server\deploy\LEEME.txt). No incluye datos PII, la base
    SQLite de desarrollo, certificados ni appsettings.Development.json.
#>
[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'paquete')
)

$ErrorActionPreference = 'Stop'
$serverDir = $PSScriptRoot
$repoDir = Split-Path $serverDir -Parent
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$runtimeName = 'aspnetcore-runtime-10.0.12-win-x64.exe'
$runtimeSha256 = 'E5D20698EA8CDDA55BCF8D6151D5B65097761506A1D8ED9AF39910CFC956E5AD'
$runtimeUrl = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.12/$runtimeName"

$stamp = Get-Date -Format 'yyyy-MM-dd'
$pkgName = "PWExtendedApp_$stamp"
$pkgDir = Join-Path $OutputRoot $pkgName
$zipPath = "$pkgDir.zip"

# No borrar un paquete que esta instalado como servicio (tiene certs\ y data\ propios).
$svc = Get-CimInstance Win32_Service -Filter "Name='PWExtendedApp' OR Name='UsuariosRetiradosDTU'" -ErrorAction SilentlyContinue |
    Where-Object { $_.PathName -like "*$pkgDir*" } | Select-Object -First 1
if ($svc) {
    throw "El servicio $($svc.Name) corre desde '$pkgDir'. Desinstalelo o use otro -OutputRoot."
}

if (Test-Path $pkgDir) { Remove-Item -LiteralPath $pkgDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null

# 1. Publicar (framework-dependent, win-x64). nuget.org explicito por si la
#    maquina solo tiene registrado el origen offline de Visual Studio.
Write-Host '==> Publicando aplicacion...' -ForegroundColor Cyan
& $dotnet publish (Join-Path $serverDir 'UsuariosRetirados.Server.csproj') `
    -c Release -r win-x64 --self-contained false -o (Join-Path $pkgDir 'app') `
    --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { throw 'Fallo dotnet publish.' }

# Nunca distribuir el secreto JWT de desarrollo.
Get-ChildItem (Join-Path $pkgDir 'app') -Filter 'appsettings.Development.json' -Recurse | Remove-Item -Force

# 2. Instalador y documentacion
Copy-Item (Join-Path $serverDir 'deploy\*') $pkgDir -Recurse
New-Item -ItemType Directory -Path (Join-Path $pkgDir 'salidas') -Force | Out-Null

# 3. ASP.NET Core Runtime (se descarga a prerequisitos\ si no esta)
$prereqDir = Join-Path $repoDir 'prerequisitos'
$runtimePath = Join-Path $prereqDir $runtimeName
if (-not (Test-Path $runtimePath)) {
    Write-Host "==> Descargando $runtimeName..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $runtimeUrl -OutFile $runtimePath -UseBasicParsing
}
$hash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
if ($hash -ne $runtimeSha256) {
    throw "SHA256 del runtime no coincide ($hash). Borre '$runtimePath' y reintente."
}
New-Item -ItemType Directory -Path (Join-Path $pkgDir 'prerequisitos') -Force | Out-Null
Copy-Item -LiteralPath $runtimePath (Join-Path $pkgDir 'prerequisitos')

# 4. ZIP
Write-Host '==> Comprimiendo...' -ForegroundColor Cyan
Compress-Archive -Path $pkgDir -DestinationPath $zipPath
$sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "==> Paquete listo: $zipPath ($sizeMb MB)" -ForegroundColor Green
