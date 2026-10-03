# Prerequisitos de despliegue

Instaladores necesarios para llevar la herramienta a un servidor Windows.
Los `.exe` de esta carpeta **no se versionan** (`.gitignore`); este `README.md`
con los links **sí se sube al repo**.

## Contenido de la carpeta

| Archivo | Versión | Tamaño | SHA256 |
|---|---|---|---|
| `dotnet-sdk-10.0.401-win-x64.exe` | SDK .NET 10.0.401 (x64) | ~205 MB | `C4C36D27DD98EDBECA236628611B617A6CB0E9623563E3FFD97F3B83952405D9` |
| `aspnetcore-runtime-10.0.12-win-x64.exe` | ASP.NET Core Runtime 10.0.12 (x64) | ~11 MB | `E5D20698EA8CDDA55BCF8D6151D5B65097761506A1D8ED9AF39910CFC956E5AD` |

Ambos están firmados digitalmente por `CN=.NET` (Authenticode válido).

## ¿Cuál instalar?

| Escenario | Instalar |
|---|---|
| El servidor va a compilar/publicar o correr `Iniciar-Servidor.cmd` (`dotnet run`) | **SDK** (incluye el runtime) |
| Se copia una carpeta `server\publish` ya publicada y solo se registra el servicio | **ASP.NET Core Runtime** (más liviano) |

Instalación silenciosa (PowerShell como Administrador):

```powershell
.\dotnet-sdk-10.0.401-win-x64.exe /install /quiet /norestart
# o
.\aspnetcore-runtime-10.0.12-win-x64.exe /install /quiet /norestart
```

Verificación:

```powershell
dotnet --list-sdks        # debe aparecer 10.0.401
dotnet --list-runtimes    # debe aparecer Microsoft.AspNetCore.App 10.0.x
```

> **Ojo**: si en el PATH aparece primero el `dotnet` x86 (`C:\Program Files (x86)\dotnet`),
> los comandos fallan con "No .NET SDKs were found". Usar siempre el x64:
> `C:\Program Files\dotnet\dotnet.exe`. `Iniciar-Servidor.cmd` ya lo resuelve.

## Links oficiales de descarga

- SDK .NET 10 (x64, instalador directo):
  <https://aka.ms/dotnet/10.0/dotnet-sdk-win-x64.exe>
- ASP.NET Core Runtime 10 (x64, instalador directo):
  <https://aka.ms/dotnet/10.0/aspnetcore-runtime-win-x64.exe>
- Página de descargas (.NET 10): <https://dotnet.microsoft.com/download/dotnet/10.0>
- URLs resueltas (builds):
  - <https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-win-x64.exe>
  - <https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.12/aspnetcore-runtime-10.0.12-win-x64.exe>

## Otros requisitos (no descargables)

- **Windows Server 2022 o superior (x64)**: el server web usa **TLS 1.3
  exclusivo** y Schannel solo lo soporta desde Windows Server 2022 / Windows 11.
  En un server anterior, editar `server\appsettings.json` y cambiar
  `Kestrel:Endpoints:Https:SslProtocols` a `[ "Tls12", "Tls13" ]` (o mínimo `Tls12`).
- **Windows PowerShell 5.1**: viene con Windows; no hay que instalar nada.
- La GUI original (`UsuariosRetiradosDTU\`) requiere Desktop Experience (no corre
  en Server Core); la app web sí corre en Core.
- **ImportExcel** ya viene embebido en `UsuariosRetiradosDTU\lib\` (no descargar).
- **Node.js no es necesario** en el servidor (el frontend es estático y el backend .NET).

## Checklist de despliegue

1. Copiar el repo (o solo `server\` + `UsuariosRetiradosDTU\`) al servidor.
2. Copiar `Empleados.txt` al servidor (dato PII, no está en git) y ajustar la ruta
   del padrón en Administración o `AppPaths:InputPath`.
3. Copiar `server\certs\` (PFX + `pfx-password.txt`, tampoco están en git) **o**
   generar un certificado nuevo en el servidor (ver `server\README.md`).
4. Instalar el SDK o el Runtime (según el escenario de la tabla de arriba).
5. Abrir el puerto HTTPS en el firewall:
   `New-NetFirewallRule -DisplayName "UsuariosRetiradosDTU 5001" -Direction Inbound -Protocol TCP -LocalPort 5001 -Action Allow`
6. Registrar el servicio:
   `.\server\Instalar-Servicio.ps1 -Action Install -Port 5001`
   (genera `Jwt__Secret`, configura Kestrel y copia el PFX a `publish\certs`).
7. Distribuir `server\certs\server.cer` a los equipos cliente e importarlo en
   `Cert:\LocalMachine\Root` para evitar la advertencia de certificado.
8. Verificar: `https://<servidor>:5001` y login con las credenciales seed
   (**cambiarlas de inmediato**).
