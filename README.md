# PW Extended App

**Herramientas web de Control de Acceso para Honeywell ProWatch.** Módulos:

- **Usuarios Retirados DTU**: filtra el padrón de empleados y genera los insumos de ProWatch DTU.
- **Ocupación Edificios**: dashboard, consulta y exportación de las marcaciones diarias
  (`prowatch.db`), con carga de los reportes Excel.

El menú lateral agrupa las pantallas por módulo; agregar una sección nueva es un botón
`.nav-tab` + un `<section class="tab-pane">` (ver `server/wwwroot/index.html`).

Lee `Empleados.txt`, lo filtra por **fecha de evento**, **estado** y **sociedad**, y genera el
reporte **XLSX**, un **TSV** opcional y el **TXT plano** que procesa la herramienta **DTU** de
ProWatch. Incluye login, historial de procesos y descargas desde el navegador, sobre **HTTPS con
TLS 1.3**.

---

## Quickstart (desarrollo)

```powershell
# Requiere .NET SDK 10 x64 (ver prerequisitos/README.md)
.\server\Iniciar-Servidor.cmd
```

- URL: **https://localhost** (certificado autofirmado: aceptar la advertencia una vez).
- Credenciales seed: `admin / Admin123!` y `operador1..5 / Operador123!` (**cambiarlas**).

Despliegue como servicio Windows: ver `server/README.md` e `Instalar-Servicio.ps1`.

---

## Características

| | |
|---|---|
| **App web** | ASP.NET Core 10 (API REST + SPA), login JWT + BCrypt |
| **HTTPS / TLS 1.3** | Kestrel solo HTTPS en el puerto 443; TLS 1.2 rechazado |
| **Historial** | Cada proceso queda en SQLite con usuario, filtros, filas y descargas |
| **Descargas** | DTU / XLSX / TSV desde el navegador, con auditoría |
| **Sin Excel** | El XLSX se genera con ClosedXML en el servidor |
| **Barrido único** | Cache de escaneo para no leer los ~165 MB dos veces |
| **Validación de estructura** | Compara el encabezado contra `server/app/estructura.json` |

---

## Salidas

| Archivo | Contenido | Delimitador | Encabezado |
|---|---|---|---|
| `Usuarios Retirados <dd-MM-yyyy>.xlsx` | `ESTADO · DOCUMENTO · NOMBRE EMPLEADO · APELLIDO EMPLEADO · NOMBRE SOCIEDAD · FECHA EVENTO` | hoja Excel | sí |
| `Usuarios Retirados <dd-MM-yyyy>.tsv` | mismas columnas | TAB | sí |
| `Usuarios Retirados DTU al <dd-MM-yyyy>.txt` | `DOCUMENTO<TAB>FECHA<TAB>T` | TAB | **no** |

> El **TXT DTU** es el artefacto crítico y su formato no debe cambiarse: lo consume ProWatch.
> Destino por defecto: `salidas\` en la raíz del repo (configurable en Administración).

---

## Estructura del proyecto

```text
empleados/
├── server/                         app web ASP.NET Core 10 (código)
│   ├── Controllers/ Services/      API y lógica (Services/Ocupacion = módulo Ocupación Edificios)
│   ├── Data/ Models/ DTOs/         EF Core (usuarios, historial, VIP) y contratos de la API
│   ├── wwwroot/                    SPA: app.js (carcasa + DTU), ocupacion.js, api.js
│   ├── app/estructura.json         estructura esperada del padrón
│   ├── certs/                      PFX + contraseña (ignorados por git)
│   ├── deploy/                     se copia al paquete: Instalar.cmd, Limpiar-Servicios.cmd, LEEME.txt
│   ├── Iniciar-Servidor.cmd        desarrollo (https://localhost)
│   ├── Instalar-Servicio.ps1       instala el servicio PWExtendedApp en este equipo (desde el repo)
│   └── Crear-Paquete.ps1           genera paquete/PWExtendedApp_<fecha>.zip para el servidor
├── data/                           bases SQLite (ignoradas por git)
│   ├── prowatch.db                 Ocupación Edificios (~800 MB)
│   ├── usuarios_retirados.db       usuarios/historial/VIP del servicio instalado
│   └── usuarios_retirados.dev.db   usuarios/historial/VIP de desarrollo
├── salidas/                        archivos generados DTU/XLSX/TSV (ignorados por git)
├── paquete/                        paquetes generados (ignorados por git)
├── prerequisitos/                  instaladores .NET 10 (binarios ignorados)
├── docs/                           documentación y presentación
└── AGENTS.md                       guía para asistentes del repo
```

Para quitar los servicios de Windows (actual y anterior) y sus reglas de firewall sin borrar
datos: `server\deploy\Limpiar-Servicios.cmd` como administrador (`-Simular` para solo ver).

---

## Privacidad y datos sensibles

Este repositorio **no incluye datos de empleados**. El `.gitignore` excluye el padrón de entrada
(`Empleados*.txt|csv|xlsx`, contiene **cédulas**), todas las salidas generadas
(`Usuarios Retirados*` y el TXT DTU), `salidas/`, `server/certs/`, la base SQLite y los
instaladores de `prerequisitos/`.

Para probar, usar un archivo fuera del repo o una muestra anonimizada.

---

## Documentación

- `server/README.md` — arquitectura, API, configuración, TLS y despliegue.
- `prerequisitos/README.md` — instaladores, links, checksums y checklist de despliegue.
- `AGENTS.md` — convenciones y reglas de negocio del repo.

---

## Legacy

La app original **PowerShell + WinForms** fue **retirada** y su carpeta eliminada: ya no forma
parte del desarrollo. Su código permanece en el historial de git y hay un respaldo portable en
`E:\CarpetaTrabajoIA\backup\UsuariosRetiradosDTU_PS_<fecha>.zip` (fuera del repo). El server web la
reemplaza con las mismas reglas de negocio y el mismo formato de salida.
