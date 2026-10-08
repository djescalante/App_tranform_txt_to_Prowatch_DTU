# AGENTS.md

**PW Extended App**: app web interna (ASP.NET Core 10 en `server/`, API REST + SPA) con login,
historial y descargas. Módulos: **Usuarios Retirados DTU** (filtra `Empleados.txt` y genera insumos
para ProWatch DTU) y **Ocupación Edificios** (marcaciones de ProWatch en `prowatch.db`). Servicio
Windows `PWExtendedApp` (el anterior `UsuariosRetiradosDTU` lo eliminan los instaladores).

> La app PowerShell + WinForms original está **retirada** (ver "Legacy"). Su código se conserva en
> el historial de git y en un ZIP de respaldo.

## Repo y datos sensibles
- Repo git con raíz en esta carpeta (`empleados/`). El `.gitignore` excluye los insumos **PII** y
  los artefactos generados: `Empleados*.txt|csv|xlsx`, `Usuarios Retirados*` (xlsx/tsv/csv/txt),
  `salidas/*` (salvo `.gitkeep`), `server/certs/`, `prerequisitos/*` (salvo `*.md`),
  `*.db*`, `**/publish/`, `*.bak.json`.
- **No commitear datos de empleados** (el `Empleados.txt` real es ~165 MB y contiene cédulas).
  Para probar, usar un archivo fuera del repo o una muestra anonimizada.
- `server/certs/` (PFX + contraseña) y `prerequisitos/*.exe` **no** se versionan.

## Archivo de entrada (quirks críticos)
- `Empleados.txt` (~165 MB, ~203.715 filas) es **CSV por comas, encabezado de 78 columnas,
  codificación Windows-1252** (sin BOM). Leerlo como UTF-8 rompe los acentos.
- Contiene **comas internas dentro de campos entre comillas** y 1 fila con comillas desbalanceadas.
- **NO usar `Split(',')`**: usar `Microsoft.VisualBasic.FileIO.TextFieldParser` en streaming
  (lo hace `CsvStreamingEngine.cs`). El split posicional da resultados distintos.
- `Empleados - copia.txt` es otra copia de datos, no código.

## Columnas usadas (por nombre, no por índice fijo)
Índices reales: `ESTADO`=0, `DOCUMENTO`=1, `NOMBRE SOCIEDAD`=9, `FECHA EVENTO`=42.
Opcionales (solo vista previa y XLSX; nunca en DTU ni TSV): `NOMBRE EMPLEADO`, `APELLIDO EMPLEADO`.
`FECHA EVENTO` tiene formato `dd/MM/yyyy`.

## Salidas
- `Usuarios Retirados <dd-MM-yyyy>.xlsx` y `.tsv` (tabulado; el TSV lleva encabezado).
- `Usuarios Retirados DTU al <dd-MM-yyyy>.txt`: **sin encabezado**, `DOCUMENTO<TAB>FECHA<TAB>T`.
  Este formato lo consume ProWatch DTU: **no cambiarlo**.
- Fechas en nombres con guiones (`/` es ilegal en Windows).
- Entrada Windows-1252; salidas **UTF-8 con BOM**.
- El XLSX mantiene `FECHA EVENTO` como texto `dd/MM/yyyy` a propósito; no convertir a tipo fecha.
- Destino por defecto: `salidas\` en la raíz del repo (configurable en Administración).

## Estructura
- `server\` — app web ASP.NET Core 10 (API REST + SPA en `wwwroot\`) con JWT/BCrypt, historial y
  descargas. Solo código: las bases **no** van ahí (`server\Data\` es código EF Core y Windows no
  distingue `data`/`Data`).
- `data\` — bases SQLite (ignoradas): `prowatch.db` (Ocupación), `usuarios_retirados.db` (servicio
  instalado desde el repo) y `usuarios_retirados.dev.db` (desarrollo, vía
  `appsettings.Development.json`). Las rutas relativas de conexión se resuelven contra la carpeta
  de la app (`Program.cs`), nunca contra el directorio actual (en un servicio sería System32).
- `server\README.md` — arquitectura, API, configuración, TLS y despliegue.
- `server\app\estructura.json` — base de estructura esperada (nombres+orden de columnas).
- `server\Iniciar-Servidor.cmd` — lanzador de desarrollo (`https://localhost`, TLS 1.3); usa
  el SDK x64 explícito y lee la contraseña del PFX desde `server\certs\pfx-password.txt`.
- `server\Instalar-Servicio.ps1` — publica y registra el servicio Windows (escribe `Jwt__Secret` y
  `Kestrel__Endpoints__Https__*` en el registro del servicio).
- `server\Crear-Paquete.ps1` — genera `paquete\PWExtendedApp_<fecha>.zip` (app publicada +
  `server\deploy\*` + runtime). `server\deploy\Limpiar-Servicios.cmd` quita los servicios
  `PWExtendedApp`/`UsuariosRetiradosDTU` y sus reglas de firewall sin borrar datos (`-Simular`).
- `prerequisitos\` — instaladores .NET 10 para el servidor (binarios ignorados; el `README.md` con
  links, checksums y checklist **sí** se versiona).
- `salidas\` — artefactos generados (ignorados por git).

## App web (server/)
- **Build/run**: usar el SDK x64 `C:\Program Files\dotnet\dotnet.exe`; el `dotnet` x86 que suele
  aparecer primero en el PATH **no tiene SDK** y `dotnet run` falla con "No .NET SDKs were found".
  `Iniciar-Servidor.cmd` ya lo resuelve.
- **Producción exige secreto JWT**: `Jwt__Secret` (env var o variable del servicio). Sin él la app
  no arranca (fail-fast). En Development lo toma de `appsettings.Development.json`.
- **Solo HTTPS / TLS 1.3**: Kestrel escucha únicamente `https://0.0.0.0:443` con
  `SslProtocols=Tls13` (TLS 1.2 rechazado). El PFX vive en `server\certs\` (ignorado; contraseña en
  `pfx-password.txt` o `Kestrel__Endpoints__Https__Certificate__Password`). No reintroducir HTTP ni `Urls`.
- **CSP estricto**: la SPA no debe usar handlers inline (`onclick=`) ni recursos externos
  (fuentes/CDN); usar `data-*` + listeners delegados. `SecurityHeadersMiddleware` aplica HSTS, CSP,
  nosniff, frame-deny, etc., y Kestrel oculta el header `Server`.
- **Paridad contractual**: el TXT DTU del server debe ser **byte-idéntico** al histórico. El parser
  es `TextFieldParser`; verificar con SHA256 (hashes de referencia en `server\README.md`). No tocar
  el formato DTU.
- **Cache de escaneo**: preview y process comparten un cache en memoria de 5 min (`ScanCache.cs`).
- **Rutas robustas**: `AppPaths.cs` resuelve `estructura.json` y `salidas\` desde repo, `publish\`
  o servicio Windows; `AppPaths:*` de `appsettings.json` son el seed inicial (luego mandan
  `AppConfigs` en SQLite).
- **Esquema SQLite**: se usa `EnsureCreated` + upgrades idempotentes; al agregar tablas/columnas hay
  que actualizar también el upgrade para DBs existentes.
- **Lista VIP**: `VipEmployees` (cédula única, `COLLATE NOCASE`) protege cédulas que **nunca** se
  exportan; preview/process las alertan y omiten (DTU/XLSX/TSV) y el job guarda
  `VipOmittedCount`/`VipOmittedDetails`. Admin gestiona (`/api/vip`), operador solo lee.
- **Ocupación Edificios** (`Services/Ocupacion/`, `wwwroot/js/ocupacion.js`): base aparte
  `prowatch.db` (`Ocupacion:DbPath`; en el repo `data\prowatch.db`, ignorada), mismo esquema que
  la app Python original (retirada y borrada). `OcupacionIngest.cs` imita a openpyxl celda por
  celda: **no cambiar** las conversiones ni el `fingerprint` SHA-1 o se duplicarán filas ya
  cargadas. Stats/filtros cacheados e invalidados al cargar/borrar; `OcupacionWarmup` los
  precalcula al arrancar. Consultar/exportar: todos; cargar/borrar: Admin.
- **Menú por módulos**: botones `.nav-tab[data-tab]` agrupados en `.nav-group` del sidebar;
  `[data-admin-only]` oculta lo que es solo de Admin; las pestañas `oc-*` las atiende
  `window.ocupacion.onShow`.
- **Admin principal**: la cuenta `admin` (`User.PrincipalAdminUsername`) no se puede eliminar, desactivar ni pasar a Operador desde la app.
- **Credenciales seed**: `admin/Admin123!` y `operador1..5/Operador123!` — cambiar en producción.
- CORS por defecto vacío = mismo origen; `Cors:AllowedOrigins` permite lista blanca o `["*"]`.
- Si `Instalar-Servicio.ps1` corre con PS 5.1, evitar sintaxis PS7 (`?.`,
  `RandomNumberGenerator::Fill`).

## Reglas de negocio (no obvias)
- Filtro de estado por defecto `Terminated`; en el archivo también existen `Active`, `Activo`,
  `Latente`, `ReportNo-Show` y **dos variantes** de "Con terminación de contrato": con tilde y sin
  tilde. La opción visible `Con terminación de contrato` expande a **ambas** (`CsvStreamingEngine`).
- Sociedades: solo `BANCOLOMBIA` (default), `NEQUI SA`, `VALORES BANCOLOMBIA`,
  `BANCA DE INVERSION BANCOLOMBIA`, en ese orden. `Sociedades` vacío = todas.
- Fecha por defecto = `LastWriteTime` del TXT − 1 día (mtime 29/09/2026 ⇒ 28/09/2026).
- Defaults actuales (Terminated + BANCOLOMBIA + 28/09/2026) ⇒ 48 registros; sin filtro de sociedad
  ⇒ 50.
- El encabezado se valida contra `estructura.json`; si faltan columnas críticas no se puede
  exportar (`SchemaValid=false`).
- La app exige al menos una sociedad y un estado seleccionados.
- Las cédulas de la **Lista VIP** nunca se exportan: se alertan y se omiten de DTU/XLSX/TSV.

## Verificación
- Build app web (SDK x64):
  `& "C:\Program Files\dotnet\dotnet.exe" build server\UsuariosRetirados.Server.csproj -c Release`
- Paridad del TXT DTU (SHA256) y smoke test de API: ver `server\README.md`.
- No hay framework de tests; la verificación es manual con los comandos anteriores.
- Regla de ejecución en el server: los `.cmd` usan `-ExecutionPolicy Bypass`; una GPO podría
  bloquearlos.

## Legacy (app PowerShell, retirada)
- La app PowerShell + WinForms (`UsuariosRetiradosDTU\`, `Usuarios-Retirados-DTU.ps1`, `specs.md`)
  ya **no** forma parte del desarrollo y su carpeta se eliminó. El código sigue en el historial de git.
- Respaldo portable: `E:\CarpetaTrabajoIA\backup\UsuariosRetiradosDTU_PS_<fecha>.zip` (fuera del
  repo; incluye `lib\ImportExcel`).
- Los hashes SHA256 del TXT DTU en `server\README.md` siguen siendo el **baseline contractual**.
