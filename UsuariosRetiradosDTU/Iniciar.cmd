@echo off
setlocal
set "APPDIR=%~dp0app"

where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo No se encontro Windows PowerShell en este equipo.
    pause
    exit /b 1
)

rem Quita la "Marca de la Web" (Zone.Identifier) del modulo embebido y los scripts.
rem .NET se niega a cargar EPPlus.dll si el archivo viene de un ZIP descargado
rem (HRESULT 0x80131515: "Operacion no admitida").
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0lib','%~dp0app' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%APPDIR%\UsuariosRetirados.ps1"

endlocal
