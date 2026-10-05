@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo   Iniciando Usuarios Retirados DTU - Servidor Web (HTTPS)
echo ============================================================
echo.
echo URL de acceso local: https://localhost
echo (Certificado autofirmado: el navegador puede pedir confirmacion)
echo.
echo Presione Ctrl+C para detener el servidor.
echo ============================================================
echo.

rem Preferir el SDK x64: el dotnet x86 que aparece primero en el PATH
rem puede no tener ningun SDK instalado ("No .NET SDKs were found").
set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" set "DOTNET_EXE=dotnet"

rem Perfil Development: toma el secreto JWT de appsettings.Development.json.
set ASPNETCORE_ENVIRONMENT=Development

rem Password del PFX (server\certs\pfx-password.txt, no versionado).
set "Kestrel__Endpoints__Https__Certificate__Password="
if exist "certs\pfx-password.txt" set /p Kestrel__Endpoints__Https__Certificate__Password=<certs\pfx-password.txt
if not defined Kestrel__Endpoints__Https__Certificate__Password (
    echo ADVERTENCIA: no se encontro certs\pfx-password.txt.
    echo Genere el certificado con el comando documentado en server\README.md.
    echo.
)

"%DOTNET_EXE%" run --no-launch-profile

pause
