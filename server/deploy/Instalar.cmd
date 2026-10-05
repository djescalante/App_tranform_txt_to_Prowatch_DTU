@echo off
rem Instala el servicio. Clic derecho > "Ejecutar como administrador".
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Instalar.ps1" %*
pause
