<div align="center">

# Usuarios Retirados DTU

**Herramienta portable para filtrar el padrón de empleados y generar los insumos que consume ProWatch DTU.**

![PowerShell](https://img.shields.io/badge/PowerShell-5.1-5391FE?logo=powershell&logoColor=white)
![Platform](https://img.shields.io/badge/Windows-Desktop-0078D6?logo=windows&logoColor=white)
![Excel](https://img.shields.io/badge/Excel-no%20requerido-brightgreen)
![Portable](https://img.shields.io/badge/deploy-copiar%20y%20usar-success)
![UI](https://img.shields.io/badge/UI-WinForms-informational)

</div>

---

## Descripción

Aplicación de escritorio que lee el padrón de empleados (`Empleados.txt`), lo filtra por
**fecha de evento**, **estado** y **sociedad**, y genera:

- un reporte **XLSX** legible,
- un **TSV** tabulado,
- y el archivo **TXT plano** que procesa la herramienta **DTU** de ProWatch.

Está pensada para correr **en un servidor Windows sin instalar nada**: se copia la carpeta y se
abre con doble clic. No necesita Excel ni acceso a internet.

> Todo el código es **PowerShell + WinForms** y apunta a **Windows PowerShell 5.1**.

---

## Características

| | |
|---|---|
| **Portable** | Una carpeta, sin instalación ni permisos de administrador |
| **Sin Excel** | Genera `.xlsx` real con un motor embebido (ImportExcel + EPPlus) |
| **Filtro por fecha** | Calendario; por defecto toma la fecha de modificación del TXT − 1 día |
| **Filtro por estado** | `Terminated` (por defecto), `Con terminación de contrato`, `ReportNo-Show` |
| **Filtro por sociedad** | 4 sociedades configurables en orden, con botones *Todas* / *Ninguna* |
| **Validación de estructura** | Avisa si el encabezado cambia respecto a una base guardada |
| **Vista previa** | Revisa las coincidencias en una grilla antes de exportar |
| **Resumen previo** | Confirma cuántas filas se van a afectar y con qué filtros |
| **Barrido único** | Lee el archivo de ~200 mil filas una sola vez |
| **UTF-8 con BOM** | Salidas compatibles con Excel y sistemas externos |

---

## Vista rápida

```text
┌──────────────────────────────────────────────────────────────────────┐
│  Usuarios Retirados DTU                                            [X] │
├──────────────────────────────────────────────────────────────────────┤
│  Archivo TXT de empleados:  [ C:\ruta\Empleados.txt        ] [Examinar]│
│  ┌ Filtros ──────────────────────────────────────────────────────────┐ │
│  │ Fecha del evento:  [ 28/09/2026 v]                                │ │
│  │ Estado (marque uno o varios):   Sociedad (marque una o varias):   │ │
│  │  [ ] Con terminación de contrato  [x] BANCOLOMBIA      [Todas]    │ │
│  │  [x] Terminated                   [ ] NEQUI SA         [Ninguna]  │ │
│  │  [ ] ReportNo-Show                [ ] VALORES BANCOLOMBIA         │ │
│  └───────────────────────────────────────────────────────────────────┘ │
│  ┌ Salida ───────────────────────────────────────────────────────────┐ │
│  │ [x] XLSX   [ ] TSV   [x] TXT DTU   [x] Mostrar resumen            │ │
│  │ Carpeta destino: [ ...\salidas                          ] [Elegir] │ │
│  └───────────────────────────────────────────────────────────────────┘ │
│  [ Vista previa ]  [ Exportar ]                          [ Cerrar ]    │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Requisitos

- **Windows** con **Desktop Experience** (la GUI no corre en Server Core).
- **Windows PowerShell 5.1** (viene de fábrica en Windows).
- Permiso de escritura en la carpeta de salida elegida.
- **No** requiere Excel, ni internet, ni módulos de la galería.

---

## Instalación portable

1. Copia la carpeta `UsuariosRetiradosDTU/` al servidor o equipo destino.
2. Asegúrate de conservar `lib/ImportExcel/` (es lo que permite generar XLSX sin Excel).
3. Ejecuta **`Iniciar.cmd`** (doble clic).

> Si una política de grupo bloquea scripts, el lanzador ya usa `-ExecutionPolicy Bypass`.
> En entornos muy restringidos podría requerir una excepción de la GPO.

---

## Uso

1. **Examinar…** y seleccionar `Empleados.txt`. La app valida la estructura, cuenta filas y
   ajusta el calendario.
2. Elegir la **fecha del evento** (por defecto, la fecha de modificación − 1 día).
3. Marcar los **estados** y las **sociedades** a incluir.
4. (Opcional) **Vista previa** para revisar las coincidencias.
5. **Exportar**. Si el resumen está activo, se confirma el detalle y se escriben los archivos.

### Modo motor (sin GUI)

El motor puede usarse por sí solo:

```powershell
. .\UsuariosRetiradosDTU\app\Motor.ps1
Import-ImportExcelModule
Export-UsuariosRetirados -Path '.\Empleados.txt' `
    -OutputDir '.\UsuariosRetiradosDTU\salidas' `
    -FechaEvento '28/09/2026' -Sociedades @('BANCOLOMBIA') -Estados @('Terminated') `
    -EmitXlsx -EmitDtu
```

---

## Salidas

| Archivo | Contenido | Delimitador | Encabezado |
|---|---|---|---|
| `Usuarios Retirados <dd-MM-yyyy>.xlsx` | `ESTADO · DOCUMENTO · NOMBRE SOCIEDAD · FECHA EVENTO` | hoja Excel | sí |
| `Usuarios Retirados <dd-MM-yyyy>.tsv` | mismas columnas | TAB | sí |
| `Usuarios Retirados DTU al <dd-MM-yyyy>.txt` | `DOCUMENTO<TAB>FECHA<TAB>T` | TAB | **no** |

> El **TXT DTU** es el artefacto crítico y su formato no debe cambiarse: lo consume ProWatch.

---

## Estructura del proyecto

```text
empleados/
├── UsuariosRetiradosDTU/
│   ├── Iniciar.cmd                 lanzador (doble clic)
│   ├── app/
│   │   ├── UsuariosRetirados.ps1   GUI WinForms
│   │   ├── Motor.ps1               parser, filtros y exportadores
│   │   ├── estructura.json         base de estructura esperada
│   │   └── config.json             preferencias locales
│   ├── lib/ImportExcel/7.8.10/     motor XLSX embebido
│   ├── salidas/                    destino por defecto
│   └── logs/
├── Usuarios-Retirados-DTU.ps1      script piloto original (CLI)
├── specs.md                        especificación funcional y técnica
└── AGENTS.md                       guía para asistentes del repo
```

---

## Privacidad y datos sensibles

Este repositorio **no incluye datos de empleados**. El `.gitignore` excluye explícitamente:

- el padrón de entrada (`Empleados*.txt` / `.csv` / `.xlsx`), que contiene **cédulas**;
- todas las salidas generadas (`Usuarios Retirados*.xlsx|tsv|csv` y el TXT DTU);
- las carpetas `salidas/` y `logs/`, y la configuración local `app/config.json`.

Para probar, usa un archivo fuera del repo o una muestra anonimizada.

---

## Verificación

```powershell
# Sintaxis sin ejecutar la GUI
powershell.exe -NoProfile -Command "[void][System.Management.Automation.Language.Parser]::ParseFile('<ruta.ps1>',[ref]$null,[ref]$null)"

# Integridad del TXT DTU
Get-FileHash '<Usuarios Retirados DTU al dd-MM-yyyy.txt>' -Algorithm SHA256
```

No hay framework de tests; la verificación es manual (sintaxis, conteo de filas y hash del TXT).

---

## Roadmap

- [x] Filtrado por fecha, estado y sociedad
- [x] Exportación a XLSX / TSV / TXT DTU
- [x] Validación de estructura y resumen previo
- [ ] **Fase 2:** validación contra la base de datos de **ProWatch** (SQL Server), confirmando el
      cambio de estado a `T` por cédula tras procesar el DTU

---

<div align="center">

**Terceros:** <a href="https://github.com/dfinke/ImportExcel">ImportExcel</a> (MIT) · EPPlus (LGPL) ·
<a href="https://learn.microsoft.com/powershell/">Windows PowerShell</a>

*Uso interno.*

</div>
