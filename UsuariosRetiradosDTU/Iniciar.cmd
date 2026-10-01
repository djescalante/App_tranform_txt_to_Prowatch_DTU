@echo off
setlocal
set "APPDIR=%~dp0app"

where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo No se encontro Windows PowerShell en este equipo.
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%APPDIR%\UsuariosRetirados.ps1"

endlocal
