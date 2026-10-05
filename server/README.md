# Usuarios Retirados DTU — Servidor Web (.NET 10)

App web cliente-servidor que reemplaza a la herramienta PowerShell original (retirada): filtra el
padrón `Empleados.txt` y genera los insumos para ProWatch DTU, con login, historial de procesos y
descargas desde el navegador.

## Arquitectura

| Componente | Rol |
|---|---|
| `Program.cs` | Host ASP.NET Core: SQLite/EF Core, JWT, CORS, estáticos y seed inicial |
| `Data/AppDbContext.cs` | Tablas `Users`, `ProcessingJobs` (auditoría), `AppConfigs` |
| `Services/CsvStreamingEngine.cs` | Lectura Windows-1252 con `TextFieldParser` y filtros (ESTADO, DOCUMENTO, NOMBRE SOCIEDAD, FECHA EVENTO) |
| `Services/ExportService.cs` | Genera TXT DTU, TSV y XLSX (ClosedXML, sin Excel instalado) |
| `Services/SchemaValidator.cs` | Valida encabezado contra `estructura.json` |
| `Services/ScanCache.cs` | Cache 5 min del último escaneo (evita doble pasada preview+process) |
| `Services/AppPaths.cs` | Resuelve `estructura.json` y `salidas\` desde repo, publish o servicio |
| `Services/JwtService.cs` | Emite JWT HS256 (7 días) |
| `Controllers/` | `Auth`, `Empleados`, `Jobs`, `Admin` |
| `wwwroot/` | SPA vanilla (login, dashboard, proceso, historial, administración) |
| `Iniciar-Servidor.cmd` | Lanzador de desarrollo (SDK x64, `https://localhost`, TLS 1.3) |
| `Instalar-Servicio.ps1` | Publica e instala como servicio Windows con `Jwt__Secret` y certificado TLS propios |

## Requisitos

- .NET SDK 10 x64 (`C:\Program Files\dotnet\dotnet.exe`). **Ojo**: si en el PATH
  aparece primero el `dotnet` x86, `dotnet run` falla con "No .NET SDKs were found".
- Windows con Desktop Experience para la GUI original (esta web no lo requiere).
- El padrón `Empleados.txt` (~158 MB, Windows-1252) accesible desde el server.

## Ejecución (desarrollo)

```powershell
.\Iniciar-Servidor.cmd          # HTTPS https://localhost (lee certs\pfx-password.txt)
# o bien:
$env:Kestrel__Endpoints__Https__Certificate__Password = (Get-Content .\certs\pfx-password.txt -Raw).Trim()
& "C:\Program Files\dotnet\dotnet.exe" run --project server --no-launch-profile
```

`Iniciar-Servidor.cmd` define `ASPNETCORE_ENVIRONMENT=Development`, que toma el
secreto JWT de `appsettings.Development.json`. En Production el secreto es
obligatorio vía `Jwt__Secret` (si falta, la app no arranca a propósito).
El server **solo escucha HTTPS en el puerto 443**; no hay HTTP.

## Configuración

`appsettings.json`:

| Clave | Uso |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQLite (`Data Source=usuarios_retirados.db`) |
| `Jwt:Issuer` / `Jwt:Audience` | Validación del token |
| `Cors:AllowedOrigins` | `[]` = mismo origen (recomendado); `["*"]` = cualquiera; o lista de orígenes |
| `AppPaths:InputPath` | Ruta por defecto del padrón (editable en Administración) |
| `AppPaths:OutputDir` | Carpeta por defecto de salidas (editable en Administración) |
| `Kestrel:Endpoints:Https:Url` | `https://0.0.0.0:443` (único listener) |
| `Kestrel:Endpoints:Https:Certificate:Path` | Ruta del PFX (`certs/server.pfx`) |
| `Kestrel:Endpoints:Https:SslProtocols` | `["Tls13"]` (TLS 1.2 y anteriores rechazados) |

Overrides por variable de entorno (doble guion bajo): `Jwt__Secret`,
`Kestrel__Endpoints__Https__Certificate__Password`, `ConnectionStrings__DefaultConnection`,
`AppPaths__InputPath`, etc. Las rutas en ejecución se guardan en la tabla
`AppConfigs` (las edita un Admin).

## TLS y certificados

- El server usa **TLS 1.3 exclusivamente** (`SslProtocols = Tls13`); un cliente
  que solo ofrezca TLS 1.2 recibe error de handshake.
- El certificado es un PFX autofirmado en `server\certs\server.pfx` (ignorado por
  git, junto con `server.cer` y `pfx-password.txt`). Para generarlo:

```powershell
$pwd = -join (1..28 | ForEach-Object { 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#%^*'[(Get-Random -Maximum 62)] })
$sec = ConvertTo-SecureString $pwd -AsPlainText -Force
$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation Cert:\CurrentUser\My `
    -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(2) -KeyExportPolicy Exportable
Export-PfxCertificate -Cert $cert -FilePath .\certs\server.pfx -Password $sec
Export-Certificate   -Cert $cert -FilePath .\certs\server.cer | Out-Null
[System.IO.File]::WriteAllText(".\certs\pfx-password.txt", $pwd)
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"
```

- La contraseña del PFX se lee de `certs\pfx-password.txt` (launcher) o de
  `Kestrel__Endpoints__Https__Certificate__Password` (servicio/entorno). **No se versiona.**
- Para que los equipos de la LAN confíen en el certificado, importar
  `certs\server.cer` en `Cert:\LocalMachine\Root` de cada cliente (o aceptar la
  advertencia una vez). Alternativa recomendada en dominio: emitir el cert con AD CS.

## Cabeceras de seguridad

El middleware `SecurityHeadersMiddleware` agrega en cada respuesta:
`Strict-Transport-Security`, `Content-Security-Policy` estricto
(`script-src 'self'`, sin `unsafe-inline` para scripts), `X-Content-Type-Options`,
`X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` y
`Cross-Origin-Opener-Policy`; además Kestrel no expone el header `Server`.
**La SPA no usa handlers inline ni recursos externos** (fuentes del sistema):
si se agregan, actualizar la CSP o el navegador los bloqueará.

## Despliegue como servicio Windows

Los instaladores de .NET 10 para el servidor están en `prerequisitos\` (ver
`prerequisitos\README.md` con links, checksums y checklist de despliegue).

```powershell
# Ejecutar en PowerShell como Administrador
.\Instalar-Servicio.ps1 -Action Install -Port 443
```

- Publica en `server\publish` (framework-dependent) y copia el PFX a `publish\certs`.
- Genera un `Jwt__Secret` aleatorio si no se pasa `-JwtSecret` y lo guarda en el
  registro del servicio junto con la configuración de Kestrel (URL HTTPS, ruta del
  PFX y su contraseña). **Anótalo**: es necesario para reinstalar sin invalidar
  las sesiones existentes.
- Parámetros opcionales `-CertPath` / `-CertPassword` si el PFX está en otro lado.
- `-Action Status` / `-Action Uninstall` para consultar o remover.

## API

Todas las rutas requieren `Authorization: Bearer <token>` salvo `login`.
Las descargas aceptan además `?token=` (necesario para `<a href>`).

| Método y ruta | Rol | Descripción |
|---|---|---|
| `POST /api/auth/login` | — | Devuelve JWT + datos del usuario |
| `GET /api/auth/me` | auth | Usuario actual |
| `POST /api/auth/change-password` | auth | Cambia la contraseña (mínimo 8, distinta a la actual) |
| `GET /api/empleados/info` | auth | Estado del padrón, columnas, fecha sugerida (mtime − 1 día) |
| `GET /api/empleados/options` | auth | Estados, sociedades y fecha sugerida |
| `POST /api/empleados/preview` | auth | Cuenta coincidencias y muestra hasta `limit` filas |
| `POST /api/empleados/process` | auth | Filtra, exporta y registra el job |
| `GET /api/jobs?limit=N` | auth | Historial de procesos |
| `GET /api/jobs/{id}/download/{dtu\|xlsx\|tsv}` | auth | Descarga la salida |
| `GET/POST/PUT /api/admin/users` | Admin | Gestión de usuarios |
| `POST /api/admin/jobs/clear` | Admin | Elimina los registros del historial de procesos (no borra archivos en disco) |
| `GET/POST /api/admin/config` | Admin | Rutas del padrón/salidas |
| `GET /api/vip` | auth | Lista VIP (cédulas protegidas) |
| `POST/PUT/DELETE /api/vip` | Admin | Alta, edición y baja de la lista VIP |

Ejemplos listos para usar en `UsuariosRetirados.Server.http`.

## Seguridad

- Seed inicial: `admin/Admin123!` y `operador1..5/Operador123!`. **Cambiar de
  inmediato en producción** (`POST /api/auth/change-password` o Administración).
- Contraseñas con BCrypt; usuarios nuevos exigen mínimo 8 caracteres y username
  `[A-Za-z0-9._-]{3,50}`.
- El secreto JWT nunca está hardcodeado en `appsettings.json`; en Production es
  obligatorio (`Jwt__Secret`).
- CORS vacío = solo mismo origen (la SPA se sirve desde el propio server).
- `FECHA EVENTO` se mantiene como texto `dd/MM/yyyy` a propósito (consistencia DTU).

## Lista VIP (cédulas protegidas)

Las cédulas de la lista VIP **nunca se exportan** (ni DTU, ni XLSX, ni TSV), aunque coincidan con
los filtros: si aparecen, la app **alerta** (preview y confirmación) y las **omite**, sin frenar el
resto del proceso.

- La gestiona un **Admin** (`+ Agregar VIP`, editar, quitar); los operadores solo consultan.
- `TotalCoinciden` de preview/process = filas **exportadas** (coincidencias − VIP).
- El job registra `VipOmittedCount` y `VipOmittedDetails` (cédulas omitidas) y el historial lo
  muestra como badge `VIP −N`.
- El cache de escaneo guarda filas crudas, así los cambios de la lista aplican de inmediato.
- Esquema: la tabla `VipEmployees` y las columnas de auditoría se crean con un **upgrade
  idempotente** al arrancar (`EnsureCreated` no altera bases existentes).

## Formato contractual
`Usuarios Retirados DTU al <dd-MM-yyyy>.txt` no lleva encabezado y cada línea es
`DOCUMENTO<TAB>FECHA<TAB>T` (UTF-8 con BOM). **No cambiarlo**: lo consume ProWatch DTU.

## Baseline contractual de salidas

El TXT DTU y el TSV deben ser **byte-idénticos** al histórico verificado (mismo `TextFieldParser`,
mismo orden de filas y encoding). La app PowerShell que generaba la referencia está **retirada**
(respaldo en `E:\CarpetaTrabajoIA\backup\UsuariosRetiradosDTU_PS_<fecha>.zip`); los hashes de
abajo quedan como baseline contractual:

```text
# Filtro 28/09/2026 + BANCOLOMBIA + Terminated (48 filas)
DTU: FFCDFB9F4C1321A4AB7AF33D5A4868129BCB2F58994385A08AE09751E963D0C8
TSV: 0BDC009677CFA2D6F53E5B31D3D105E2FBD962C1B4B10747DB14B0B99F2D7318
```

Verificación: procesar con el server y comparar `Get-FileHash <txt> -Algorithm SHA256` contra el
valor de arriba. Casos cubiertos: filtro default (48 filas), "Con terminación de contrato" (expande
con y sin tilde), sociedades `TODAS` (50 filas) y 0 coincidencias.

## Datos sensibles

`Empleados.txt`, las salidas (`Usuarios Retirados*`, `Usuarios Retirados DTU al*`),
`server/usuarios_retirados.db*` y `server/publish/` están excluidos por `.gitignore`.
**No commitear PII.**
