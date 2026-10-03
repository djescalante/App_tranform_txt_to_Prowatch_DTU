# AGENTS.md

Herramienta interna para filtrar el padrón de empleados `Empleados.txt` y generar insumos para ProWatch DTU. Todo el código es PowerShell + WinForms.

## Repo y datos sensibles
- Es un repo git con raíz en esta carpeta (`empleados/`). El `.gitignore` excluye los insumos **PII** y los artefactos generados: `Empleados*.txt|csv|xlsx`, `Usuarios Retirados*` (xlsx/tsv/csv/txt), `UsuariosRetiradosDTU/salidas/*` (salvo `.gitkeep`), `UsuariosRetiradosDTU/logs/*`, `UsuariosRetiradosDTU/app/config.json` y `*.bak.json`.
- **No commitear datos de empleados** (el `Empleados.txt` real es ~158 MB y contiene cédulas). Para probar, usar un archivo fuera del repo o una muestra anonimizada.
- `UsuariosRetiradosDTU/lib/ImportExcel/7.8.10/` **sí** se versiona (módulo embebido, ~4.6 MB) para que la app sea portable/offline.

## Entorno y compatibilidad
- **Destino: Windows PowerShell 5.1** (el server no tiene PS7). El código debe seguir siendo 5.1-compatible; no usar sintaxis solo de PS7.
- La máquina de desarrollo sí tiene PS7 (`pwsh`). **Probar siempre con `powershell.exe`**, no `pwsh`.
- WinForms requiere Desktop Experience (no corre en Server Core).
- **Mark-of-the-Web**: si la carpeta viene de un ZIP descargado, `EPPlus.dll` queda con `Zone.Identifier` y .NET no lo carga (`HRESULT 0x80131515`); `ImportExcel` no importa y `Export-Excel` no existe. Mitigado: `Iniciar.cmd` y `Import-ImportExcelModule` llaman `Unblock-File` sobre `lib\`. Nunca asumir que el archivo está desbloqueado en una máquina nueva.

## Archivo de entrada (quirks críticos)
- `Empleados.txt` (~165 MB, ~203.715 filas) es **CSV por comas, con encabezado de 78 columnas, codificación Windows-1252** (sin BOM). Leerlo como UTF-8 rompe los acentos.
- Contiene **comas internas dentro de campos entre comillas** y 1 fila con comillas desbalanceadas.
- **NO usar `Split(',')` ni delimitar por posición simple**: usar `Microsoft.VisualBasic.FileIO.TextFieldParser` en streaming (lo hace `Motor.ps1`). El split posicional da resultados distintos.
- `Empleados - copia.txt` es otra copia de datos, no código.

## Columnas usadas (por nombre, no por índice fijo idealmente)
Índices reales en el encabezado: `ESTADO`=0, `DOCUMENTO`=1, `NOMBRE SOCIEDAD`=9, `FECHA EVENTO`=42. `FECHA EVENTO` tiene formato `dd/MM/yyyy`.

## Salidas
- `Usuarios Retirados <dd-MM-yyyy>.xlsx` y `.tsv` (tabulado; el TSV lleva encabezado).
- `Usuarios Retirados DTU al <dd-MM-yyyy>.txt`: **sin encabezado**, `DOCUMENTO<TAB>FECHA<TAB>T`. Este formato es el que consume ProWatch DTU: **no cambiarlo**.
- Fechas en nombres con guiones (`/` es ilegal en Windows).
- Entrada Windows-1252; salidas **UTF-8 con BOM**.
- El XLSX mantiene `FECHA EVENTO` como texto `dd/MM/yyyy` a propósito (consistencia con el DTU); no convertir a tipo fecha sin pedido explícito.

## Estructura
- `UsuariosRetiradosDTU\Iniciar.cmd` — lanzador (doble clic, consola oculta).
- `UsuariosRetiradosDTU\app\Motor.ps1` — motor. API: `Import-ImportExcelModule`, `Get-EmpleadoResumen`, `Select-EmpleadoFilas`, `Write-UsuariosRetirados`, `Export-UsuariosRetirados` (compat) y estructura: `Get-EmpleadoEncabezado`, `Get-EstructuraBase`, `Save-EstructuraBase`, `Compare-Estructura`.
- `UsuariosRetiradosDTU\app\UsuariosRetirados.ps1` — GUI WinForms (todo el flujo).
- `UsuariosRetiradosDTU\app\estructura.json` — base de estructura esperada (nombres+orden de columnas); respaldo en `estructura.bak.json`.
- `UsuariosRetiradosDTU\app\config.json` — persiste InputPath, OutputDir, Estados, Sociedades, MostrarResumenExport.
- **Gotcha PS 5.1**: pasar un `List[object]` a un parámetro `[object]` lanza "Los tipos de argumentos no coinciden"; tipar el parámetro como `IEnumerable` (por eso `Write-UsuariosRetirados -Filas` es `IEnumerable`).
- `UsuariosRetiradosDTU\lib\ImportExcel\7.8.10\` — módulo embebido (incluye `EPPlus.dll`) para generar XLSX **sin Excel instalado**. Cargar desde `lib\`; no depender de PSGallery ni de Excel COM en el server.
- `Usuarios-Retirados-DTU.ps1` — script piloto original (referencia, solo CLI).
- `salidas\` y `logs\` — artefactos; `logs\` está vacío.

## Reglas de negocio (no obvias)
- Filtro de estado por defecto `Terminated`; en el archivo también existen `Active`, `Activo`, `Latente`, `ReportNo-Show` y **dos variantes** de "Con terminación de contrato": con tilde y sin tilde. La opción visible `Con terminación de contrato` en la GUI expande a **ambas** (`$script:EstadoMapa` en la GUI).
- Sociedades: solo `BANCOLOMBIA` (default), `NEQUI SA`, `VALORES BANCOLOMBIA`, `BANCA DE INVERSION BANCOLOMBIA`, en ese orden. `Sociedades` vacío = todas.
- Fecha por defecto = `LastWriteTime` del TXT − 1 día (con `Empleados.txt` mtime 29/09/2026 ⇒ 28/09/2026).
- Defaults actuales (Terminated + BANCOLOMBIA + 28/09/2026) ⇒ 48 registros; sin filtro de sociedad ⇒ 50.
- Al cargar se valida la estructura del encabezado contra `estructura.json`; si difiere, la GUI ofrece Continuar/Cancelar/Actualizar base (con respaldo). Si faltan columnas críticas, bloquea exportar.
- La GUI exige al menos una sociedad marcada (el motor sí trata `Sociedades` vacío como "todas").
- Export es **single-scan**: `Select-EmpleadoFilas` cachea las coincidencias y `Write-UsuariosRetirados` escribe.

## Verificación
- Sintaxis sin ejecutar la GUI:
  `powershell.exe -NoProfile -Command "[void][System.Management.Automation.Language.Parser]::ParseFile('<ruta.ps1>',[ref]$null,[ref]$null)"`
- Prueba del motor (sin GUI): dot-source `Motor.ps1`, `Import-ImportExcelModule`, y llamar `Export-UsuariosRetirados -PreviewLimit <N>` para contar sin escribir archivos.
- Verificar que el TXT DTU siga **byte-idéntico**: comparar con `Get-FileHash <txt> -Algorithm SHA256`.
- No hay framework de tests; la verificación es manual con los comandos anteriores.
- Regla de ejecución en el server: el `.cmd` usa `-ExecutionPolicy Bypass`; una GPO podría bloquearlo.
