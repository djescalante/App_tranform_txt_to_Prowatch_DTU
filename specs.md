# specs.md — Especificación funcional y técnica

Aplicación **Usuarios Retirados DTU**. Filtra el padrón de empleados y genera insumos para
**ProWatch DTU** (Honeywell). Implementada en **PowerShell + WinForms**, portable, para
**Windows PowerShell 5.1** en servidor, sin instalación ni Excel requerido.

> Este documento es la especificación del comportamiento observable. Para el "cómo trabajar
> en el repo" ver `AGENTS.md`.

---

## 1. Alcance

1. Leer `Empleados.txt` (CSV por comas, con encabezado).
2. Filtrar registros por **fecha de evento**, **estado** y **sociedad**.
3. Exportar:
   - **XLSX** (reporte legible).
   - **TSV** (tabulado, opcional).
   - **TXT DTU** (insumo que consume la herramienta DTU de ProWatch).
4. **Validar la estructura** del archivo contra una base guardada en cada carga.
5. Mostrar un **resumen de la selección** (con el número de filas afectadas) antes de exportar.
6. Validar (fase posterior) contra la base de datos de ProWatch.

Fuera de alcance: modificar el archivo de entrada; escribir en ProWatch.

---

## 2. Entrada

| Atributo | Valor |
|---|---|
| Archivo | `Empleados.txt` |
| Delimitador | Coma (`,`) |
| Encabezado | Sí, 78 columnas |
| Codificación | Windows-1252 (ANSI, sin BOM) |
| Volumen | ~203.715 filas / ~165 MB |
| Fecha evento | Texto `dd/MM/yyyy` |

**Quirks obligatorios:**
- Hay **comas dentro de campos entre comillas** y **1 fila con comillas desbalanceadas**.
  Debido a esto **no se puede usar `Split(',')` ni corte posicional**; el parseo usa
  `Microsoft.VisualBasic.FileIO.TextFieldParser` en streaming.
- Leer como UTF-8 rompe acentos (ej. `SÍ`, `CASTAÑEDA`).

**Columnas utilizadas** (identificadas por nombre en el encabezado):

| Nombre | Índice |
|---|---|
| `ESTADO` | 0 |
| `DOCUMENTO` | 1 |
| `NOMBRE SOCIEDAD` | 9 |
| `FECHA EVENTO` | 42 |

---

## 3. Reglas de negocio

### 3.1 Estado
- Opciones visibles en la GUI: `Con terminación de contrato`, `Terminated`, `ReportNo-Show`.
- Por defecto: **solo `Terminated`**.
- La opción visible `Con terminación de contrato` **expande de forma transparente a las dos
  variantes** reales del archivo:
  - `Con terminación de contrato` (con tilde)
  - `Con terminacion de contrato` (sin tilde)
- El filtro incluye el registro si su `ESTADO` pertenece al conjunto expandido.
- Comparación de texto: no sensible a mayúsculas (`OrdinalIgnoreCase`).

### 3.2 Sociedad
- Lista fija, en este orden:
  1. `BANCOLOMBIA` — **marcada por defecto**
  2. `NEQUI SA`
  3. `VALORES BANCOLOMBIA`
  4. `BANCA DE INVERSION BANCOLOMBIA`
- Botones **Todas** / **Ninguna**.
- Sin sociedades marcadas ⇒ **0 resultados** (el conjunto de sociedades es obligatorio).

### 3.3 Fecha de evento
- Selección por calendario (`DateTimePicker`, formato `dd/MM/yyyy`).
- Valor por defecto = **fecha de modificación del TXT − 1 día**
  (`(Get-Item <TXT>).LastWriteTime.Date.AddDays(-1)`).
- Los límites del calendario se amplían para incluir el default:
  `MinDate = min(FechaMin, default)` y `MaxDate = max(FechaMax, default)`.
- Coincidencia por igualdad exacta de la cadena `dd/MM/yyyy`.
- Ejemplo (estado actual del archivo): mtime `29/09/2026` ⇒ default `28/09/2026`.

### 3.4 Combinación de filtros
Un registro se exporta si y solo si: `ESTADO ∈ estados` **Y** `NOMBRE SOCIEDAD ∈ sociedades`
**Y** `FECHA EVENTO = fecha`. La comparación es sobre valores recortados (`Trim()`).

---

## 4. Validación de estructura al cargar

- Base de referencia en `app\estructura.json`: **solo nombres + orden** de columnas
  (+ metadatos informativos `CapturadaDe`, `FechaCaptura`, `TotalColumnas`).
- Se captura automáticamente la primera vez que no existe.
- En cada carga se compara el encabezado real con la base (`Compare-Estructura`).
  - **Igual** → se registra en el log y continúa.
  - **Distinta** → diálogo con el detalle y botones **Continuar / Cancelar / Actualizar estructura base**.
    Si faltan columnas críticas, "Continuar" queda deshabilitado.
- **Actualizar estructura base**: pide confirmación, guarda un respaldo en
  `app\estructura.bak.json` y sobrescribe la base con el encabezado actual.
- Si faltan columnas requeridas (`ESTADO`, `DOCUMENTO`, `NOMBRE SOCIEDAD`, `FECHA EVENTO`),
  la exportación y la vista previa quedan **bloqueadas**.

## 5. Resumen previo a exportar

- El botón **Exportar** hace **un solo barrido** (`Select-EmpleadoFilas`) que cachea en memoria
  las filas coincidentes.
- Si la casilla **"Mostrar resumen antes de exportar"** está marcada (por defecto), aparece un
  diálogo con: **filas a exportar (N)**, fecha, estados, sociedades, formatos, carpeta destino y
  archivo origen, con botones **Exportar / Cancelar**.
- La casilla se persiste como `MostrarResumenExport` en `config.json`. Desactivarla salta el diálogo.
- La sociedad es **obligatoria en la GUI**: si no hay ninguna marcada, se avisa y no se exporta
  (usar el botón "Todas" para incluir las 4).

## 6. Salidas

Codificación de todas las salidas: **UTF-8 con BOM**. Carpeta destino configurable
(por defecto `UsuariosRetiradosDTU\salidas\`).

| Archivo | Contenido | Delimitador | Encabezado |
|---|---|---|---|
| `Usuarios Retirados <dd-MM-yyyy>.xlsx` | `ESTADO, DOCUMENTO, NOMBRE SOCIEDAD, FECHA EVENTO` | hoja Excel | sí |
| `Usuarios Retirados <dd-MM-yyyy>.tsv` | mismas 4 columnas | TAB | sí |
| `Usuarios Retirados DTU al <dd-MM-yyyy>.txt` | `DOCUMENTO`, `FECHA EVENTO`, `T` | TAB | **no** |

- `<dd-MM-yyyy>` es la fecha filtrada con guiones (`/` es ilegal en nombres de archivo).
  Si no hubiera filtro de fecha, la etiqueta es `Todas`.
- En el **TXT DTU** la tercera columna es siempre la constante `T`.
- El **TXT DTU es el artefacto crítico**: su formato no debe cambiarse (lo consume ProWatch).
  Cambios deben validarse con `Get-FileHash -Algorithm SHA256` contra una corrida conocida.
- El XLSX se genera con **ImportExcel embebido (7.8.10, incluye `EPPlus.dll`)**; no requiere
  Excel instalado.
- `FECHA EVENTO` se mantiene como **texto** `dd/MM/yyyy` en todas las salidas (consistencia
  con el DTU).
- Si la cantidad de coincidencias es 0, no se genera XLSX; sí se crean TSV/TXT vacíos.

---

## 7. Arquitectura

```
UsuariosRetiradosDTU\
├─ Iniciar.cmd                     lanzador (doble clic, consola oculta, -ExecutionPolicy Bypass)
├─ app\
│  ├─ UsuariosRetirados.ps1        GUI WinForms y flujo completo
│  ├─ Motor.ps1                    parser CSV, resumen, filtros, exportadores
│  ├─ estructura.json              base de estructura esperada (nombres+orden de columnas)
│  └─ config.json                  persistencia (InputPath, OutputDir, Estados, Sociedades, MostrarResumenExport)
├─ lib\ImportExcel\7.8.10\         módulo embebido para XLSX
├─ salidas\                        destino por defecto
└─ logs\                           (reservado)
```

`Usuarios-Retirados-DTU.ps1` (raíz) es el **script piloto original** (solo CLI), conservado
como referencia; la app es la evolución.

---

## 8. API del motor (`Motor.ps1`)

### `Import-ImportExcelModule [-LibRoot <ruta>]`
Importa el `ImportExcel.psd1` encontrado bajo `lib\` (o `-LibRoot`). Devuelve la ruta del
manifiesto. Debe llamarse antes de exportar XLSX.

### Estructura
- `Get-EmpleadoEncabezado -Path <txt>` → `string[]` (solo lee el primer registro).
- `Get-EstructuraBase -Path <json>` → objeto `{ Columnas, CapturadaDe, FechaCaptura }` o `$null`.
- `Save-EstructuraBase -Path <json> -Columnas <string[]> -Origen <str> [-Respaldo]` →
  graba la base; con `-Respaldo` copia antes a `estructura.bak.json`.
- `Compare-Estructura -Esperada <string[]> -Actual <string[]>` → `{ Igual, TotalEsperado,
  TotalActual, Faltantes, Extra, Reordenadas, RequeridasFaltantes }` (comparación case-insensitive).

### `Get-EmpleadoResumen -Path <txt> [-EncodingIn Windows-1252] [-ProgressEvery 20000] [-OnProgress <sb>]`
Un solo barrido del archivo. Devuelve:
```
TotalFilas : int
Malformed  : int          # filas con comillas desbalanceadas omitidas
Sociedades : string[]     # valores distintos, ordenados
Estados    : string[]     # valores distintos, ordenados
FechaMin   : datetime|null
FechaMax   : datetime|null
```

### `Select-EmpleadoFilas`
Un solo barrido que filtra y **cachea** las filas coincidentes.
```
-Path <txt>              (obligatorio)
[-EncodingIn Windows-1252]
[-FechaEvento <dd/MM/yyyy>]   # vacío = sin filtro de fecha
[-Sociedades <string[]>]      # vacío = todas
[-Estados <string[]>]         # default @('Terminated')
[-MaxFilas <int>]             # 0 = sin límite
[-ProgressEvery 20000] [-OnProgress <sb>]
```
Devuelve `{ TotalFilas, Malformed, Coinciden, Filas (List[string[4]]), FechaTag }`.

### `Write-UsuariosRetirados`
Escribe las salidas desde `Filas` (o cualquier colección de `string[4]`).
```
-Filas <IEnumerable>  (obligatorio)   -OutputDir <dir>  (obligatorio)
[-FechaTag <dd-MM-yyyy|Todas>] [-EmitXlsx] [-EmitTsv] [-EmitDtu]
```
Devuelve `{ Files, Rows }`. Nota: en PS 5.1 el parámetro `-Filas` debe tiparse `IEnumerable`
(pasar un `List[object]` a `[object]` lanza "Los tipos de argumentos no coinciden").

### `Export-UsuariosRetirados`
Envuelve `Select-EmpleadoFilas` + `Write-UsuariosRetirados` (compatibilidad CLI/piloto).
```
-Path <txt>              (obligatorio)
-OutputDir <dir>         (obligatorio)
[-EncodingIn Windows-1252]
[-FechaEvento <dd/MM/yyyy>]   # vacío = sin filtro de fecha
[-Sociedades <string[]>]      # vacío = todas
[-Estados <string[]>]         # default @('Terminated')
[-EmitXlsx] [-EmitTsv] [-EmitDtu]
[-PreviewLimit <int>]         # >0 = solo vista previa, no escribe archivos
[-ProgressEvery 20000]
[-OnProgress <scriptblock>]   # recibe el número de filas procesadas
```
Devuelve:
```
TotalFilas : int
Malformed  : int
Coinciden  : int          # filas que cumplen los filtros
Preview    : bool
Rows       : List[object] # objetos ESTADO/DOCUMENTO/NOMBRE SOCIEDAD/FECHA EVENTO (en preview)
Files      : string[]     # rutas generadas
FechaTag   : string       # <dd-MM-yyyy> o 'Todas'
```

---

## 9. Interfaz gráfica

Una sola ventana ("Usuarios Retirados DTU"):

1. **Archivo TXT**: caja de texto + `Examinar...`. Al cargar: valida la **estructura** (ver 4),
   ejecuta `Get-EmpleadoResumen` (barra en modo *marquee*), muestra conteo, rango de fechas y
   ajusta el calendario.
2. **Fecha del evento**: calendario; el default es mtime−1 (ver 3.3).
3. **Estado**: `CheckedListBox` (ver 3.1).
4. **Sociedad**: `CheckedListBox` fijo + botones `Todas`/`Ninguna` (ver 3.2).
5. **Salida**: casillas `XLSX`, `TSV`, `TXT DTU (obligatorio)`, `Mostrar resumen antes de exportar`
   + carpeta destino.
6. **Vista previa**: hasta 200 coincidencias en un `DataGridView`.
7. **Exportar**: barrido único; si el resumen está activo, muestra el diálogo con el número de
   filas (ver 5); luego escribe y ofrece abrir la carpeta.
8. **Log**: caja de solo lectura con marcas de tiempo.

**Validaciones previas a exportar:** al menos un formato; al menos un estado; al menos una
sociedad (si no, aviso y no exporta); estructura aprobada.

**Persistencia (`config.json`):** `InputPath`, `OutputDir`, `Estados` (etiquetas),
`Sociedades` y `MostrarResumenExport`. Se guarda al exportar y al cerrar; se aplica al iniciar,
y si falta un valor se usan los defaults (`Terminated` + `BANCOLOMBIA` + resumen activo).

---

## 10. Rendimiento y robustez

- Lectura en **streaming** con `TextFieldParser`; sin cargar el archivo completo en memoria.
- La exportación es **single-scan**: `Select-EmpleadoFilas` cachea en memoria las filas
  coincidentes (`List[string[4]]`) y `Write-UsuariosRetirados` las escribe. Con filtros amplios
  (todas las sociedades y/o sin fecha) puede cachear hasta ~203k filas.
- Tiempos de referencia en PS 5.1 (203.715 filas): **resumen ≈ 19 s**, **export ≈ 13 s**.
- Filas con comillas desbalanceadas se cuentan en `Malformed` y se omiten, sin abortar.
- Si falta una columna esperada en el encabezado, se lanza error explícito.

---

## 11. Despliegue portable

- Copiar la carpeta `UsuariosRetiradosDTU\` al servidor y ejecutar `Iniciar.cmd`.
- Requisitos del server: **Windows PowerShell 5.1** y **Desktop Experience** (WinForms no
  corre en Server Core).
- El `.cmd` usa `-ExecutionPolicy Bypass`; **una GPO podría bloquearlo**.
- No requiere Excel ni acceso a PSGallery (ImportExcel va embebido).
- Se requiere permiso de escritura en la carpeta de salida elegida.

---

## 12. Fase 2 — Validación contra ProWatch (pendiente)

Objetivo: confirmar que las cédulas exportadas cambiaron de estado a `T` en ProWatch
(Honeywell) tras procesar el TXT DTU con la herramienta DTU.

Propuesta:
1. Leer los `DOCUMENTO` del TXT DTU generado.
2. Consultar la base ProWatch (SQL Server) y obtener el estado por cédula.
3. Emitir un XLSX de validación: `DOCUMENTO | EN ARCHIVO | EN PROWATCH | ESTADO PROWATCH | COINCIDE`.

Decisiones pendientes: método de acceso (SQL directo vs. solo resultado del DTU), credenciales,
esquema/tablas de ProWatch.

---

## 13. Criterios de aceptación (fase 1)

- [ ] Con defaults (`Terminated` + `BANCOLOMBIA` + `28/09/2026`) se obtienen **48 registros**.
- [ ] El **TXT DTU** conserva el formato `DOCUMENTO<TAB>FECHA<TAB>T`, sin encabezado.
- [ ] `Con terminación de contrato` incluye ambas variantes (con y sin tilde).
- [ ] La fecha por defecto es `LastWriteTime` del TXT − 1 día.
- [ ] Los tres formatos se generan en UTF-8 con BOM y con los nombres indicados.
- [ ] La GUI arranca y exporta en **Windows PowerShell 5.1**.
- [ ] Al cargar se valida la estructura; con el encabezado actual no advierte.
- [ ] Si el encabezado cambia, aparece el diálogo y "Actualizar estructura base" crea
      `estructura.bak.json`.
- [ ] Con "Mostrar resumen antes de exportar" activo, aparece el diálogo con el conteo correcto
      (48 con los defaults).
