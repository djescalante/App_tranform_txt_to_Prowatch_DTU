#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Elimina los servicios de Windows y las reglas de firewall de PW Extended App
    (y de su nombre anterior, Usuarios Retirados DTU).

.DESCRIPTION
    Quita:
      - los servicios "PWExtendedApp" y "UsuariosRetiradosDTU" (los detiene primero;
        si no se detienen a tiempo, termina su proceso),
      - las reglas de firewall "PW Extended App <puerto>" y "UsuariosRetiradosDTU <puerto>".

    NO borra datos: se conservan data\ (usuarios, historial, Lista VIP, prowatch.db),
    certs\, salidas\ y la aplicacion. Para volver a instalar basta ejecutar el instalador.

    Compatible con Windows PowerShell 5.1.

.PARAMETER Simular
    Solo muestra lo que se eliminaria, sin cambiar nada.

.EXAMPLE
    .\Limpiar-Servicios.ps1 -Simular
    .\Limpiar-Servicios.ps1
#>
[CmdletBinding()]
param(
    [switch]$Simular
)

$ServiceNames = @('PWExtendedApp', 'UsuariosRetiradosDTU')
$FirewallPatterns = @('PW Extended App *', 'UsuariosRetiradosDTU *')

$accion = if ($Simular) { 'Se eliminaria' } else { 'Eliminando' }
$cambios = 0

Write-Host "==> Servicios" -ForegroundColor Cyan
foreach ($name in $ServiceNames) {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
    if (-not $svc) {
        Write-Host "    '$name': no esta instalado." -ForegroundColor DarkGray
        continue
    }
    $cambios++
    Write-Host "    $accion '$name' ($($svc.State)) -> $($svc.PathName)" -ForegroundColor Yellow
    if ($Simular) { continue }

    if ($svc.State -ne 'Stopped') {
        Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Service -Name $name).Status -ne 'Stopped' -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 500
        }
        $procId = (Get-CimInstance Win32_Service -Filter "Name='$name'").ProcessId
        if ($procId -and $procId -ne 0) {
            Write-Host "      No se detuvo a tiempo: terminando el proceso $procId." -ForegroundColor Yellow
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
    sc.exe delete $name | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      Eliminado." -ForegroundColor Green
    } else {
        Write-Warning "sc.exe delete '$name' devolvio $LASTEXITCODE. Cierre la consola de Servicios (services.msc) y reintente."
    }
}

Write-Host "==> Reglas de firewall" -ForegroundColor Cyan
$rules = foreach ($pattern in $FirewallPatterns) {
    Get-NetFirewallRule -DisplayName $pattern -ErrorAction SilentlyContinue
}
if (-not $rules) {
    Write-Host "    No hay reglas de la aplicacion." -ForegroundColor DarkGray
}
foreach ($rule in $rules) {
    $cambios++
    Write-Host "    $accion la regla '$($rule.DisplayName)'" -ForegroundColor Yellow
    if (-not $Simular) {
        Remove-NetFirewallRule -Name $rule.Name
        Write-Host "      Eliminada." -ForegroundColor Green
    }
}

Write-Host ""
if ($cambios -eq 0) {
    Write-Host "No habia nada que limpiar." -ForegroundColor Green
} elseif ($Simular) {
    Write-Host "Simulacion: $cambios elemento(s). Ejecute sin -Simular para eliminarlos." -ForegroundColor Cyan
} else {
    Write-Host "Limpieza terminada. Los datos (data\, certs\, salidas\) se conservan." -ForegroundColor Green
}
