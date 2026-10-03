@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo   Iniciando Usuarios Retirados DTU - Servidor Web
echo ============================================================
echo.
echo URL de acceso local: http://localhost:5000
echo.
echo Presione Ctrl+C para detener el servidor.
echo ============================================================
echo.

where dotnet >nul 2>&1
if not errorlevel 1 (
    dotnet run --no-build
) else (
    "C:\Program Files\dotnet\dotnet.exe" run --no-build
)

pause
