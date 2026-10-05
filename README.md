# Usuarios Retirados DTU

**App web para filtrar el padrón de empleados y generar los insumos que consume ProWatch DTU.**

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
| `Usuarios Retirados <dd-MM-yyyy>.xlsx` | `ESTADO · DOCUMENTO · NOMBRE SOCIEDAD · FECHA EVENTO` | hoja Excel | sí |
| `Usuarios Retirados <dd-MM-yyyy>.tsv` | mismas columnas | TAB | sí |
| `Usuarios Retirados DTU al <dd-MM-yyyy>.txt` | `DOCUMENTO<TAB>FECHA<TAB>T` | TAB | **no** |

> El **TXT DTU** es el artefacto crítico y su formato no debe cambiarse: lo consume ProWatch.
> Destino por defecto: `salidas\` en la raíz del repo (configurable en Administración).

---

## Estructura del proyecto

```text
empleados/
├── server/                     app web ASP.NET Core 10
│   ├── app/estructura.json     base de estructura esperada
│   ├── Controllers/ Services/  API y lógica (parser, export, cache, TLS)
│   ├── wwwroot/                SPA (login, dashboard, proceso, historial, admin)
│   ├── certs/                  PFX + contraseña (ignorados por git)
│   ├── Iniciar-Servidor.cmd    lanzador de desarrollo (HTTPS 443)
│   └── Instalar-Servicio.ps1   registro como servicio Windows
├── prerequisitos/              instaladores .NET 10 (binarios ignorados)
├── salidas/                    artefactos generados (ignorados por git)
├── Empleados.txt               padrón de entrada (PII, no versionado)
└── AGENTS.md                   guía para asistentes del repo
```

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

La app original **PowerShell + WinForms** (`UsuariosRetiradosDTU\`) fue **retirada**: ya no forma
parte del desarrollo. Su código permanece en el historial de git y hay un respaldo portable en
`E:\CarpetaTrabajoIA\backup\UsuariosRetiradosDTU_PS_<fecha>.zip` (fuera del repo). El server web la
reemplaza con las mismas reglas de negocio y el mismo formato de salida.
