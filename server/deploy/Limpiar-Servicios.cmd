@echo off
rem Elimina los servicios y reglas de firewall de PW Extended App (no borra datos).
rem Clic derecho > "Ejecutar como administrador". Para solo ver que haria: Limpiar-Servicios.cmd -Simular
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Limpiar-Servicios.ps1" %*
pause
