using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PWExtendedApp.Server.Services;
using PWExtendedApp.Server.Services.Ocupacion;

namespace PWExtendedApp.Server.Controllers;

/// <summary>
/// Módulo "Ocupación Edificios": consulta de marcaciones de ProWatch cargadas desde
/// los Excel diarios. Consultar y exportar: cualquier usuario; cargar y borrar: Admin.
/// </summary>
[Authorize]
[ApiController]
[Route("api/ocupacion")]
public class OcupacionController : ControllerBase
{
    private const long MaxUploadBytes = 500L * 1024 * 1024;

    private readonly OcupacionService _service;
    private readonly AuditService _audit;

    public OcupacionController(OcupacionService service, AuditService audit)
    {
        _service = service;
        _audit = audit;
    }

    /// <summary>Filtros usados, para la bitácora (solo los que tienen valor).</summary>
    private static string Describe(OcupacionFiltro f)
    {
        var parts = new List<string>();
        void Add(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{name}={value.Trim()}"); }
        Add("desde", f.FechaDesde); Add("hasta", f.FechaHasta); Add("empresa", f.Empresa); Add("ciudad", f.Ciudad);
        Add("sede", f.Sede); Add("panel", f.Panel); Add("cedula", f.Cedula); Add("nombre", f.Nombre);
        return parts.Count > 0 ? string.Join("; ", parts) : "sin filtros";
    }

    private static OcupacionFiltro Filtro(string? fechaDesde, string? fechaHasta, string? empresa, string? ciudad,
        string? sede, string? panel, string? cedula, string? nombre) =>
        new(fechaDesde, fechaHasta, empresa, ciudad, sede, panel, cedula, nombre);

    [HttpGet("stats")]
    public async Task<ActionResult<OcupacionStats>> Stats(
        string? fechaDesde, string? fechaHasta, string? empresa, string? ciudad,
        string? sede, string? panel, string? cedula, string? nombre, CancellationToken ct)
    {
        return Ok(await _service.GetStatsAsync(
            Filtro(fechaDesde, fechaHasta, empresa, ciudad, sede, panel, cedula, nombre), ct));
    }

    [HttpGet("filtros")]
    public async Task<ActionResult<OcupacionFiltros>> Filtros(CancellationToken ct) =>
        Ok(await _service.GetFiltrosAsync(ct));

    [HttpGet("registros")]
    public async Task<ActionResult<OcupacionPagina>> Registros(
        string? fechaDesde, string? fechaHasta, string? empresa, string? ciudad,
        string? sede, string? panel, string? cedula, string? nombre,
        int page = 1, int perPage = 50, string sort = "fecha", string order = "desc")
    {
        var filtro = Filtro(fechaDesde, fechaHasta, empresa, ciudad, sede, panel, cedula, nombre);
        var result = _service.GetRegistros(filtro, page, perPage, sort, order);

        // Búsquedas de una persona (por cédula o nombre): se auditan; se omiten las demás páginas.
        if (page == 1 && (!string.IsNullOrWhiteSpace(cedula) || !string.IsNullOrWhiteSpace(nombre)))
        {
            await _audit.LogAsync("ocupacion_consulta", $"{Describe(filtro)} -> {result.Total} marcaciones");
        }
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        string? fechaDesde, string? fechaHasta, string? empresa, string? ciudad,
        string? sede, string? panel, string? cedula, string? nombre, string formato = "xlsx",
        CancellationToken ct = default)
    {
        var filtro = Filtro(fechaDesde, fechaHasta, empresa, ciudad, sede, panel, cedula, nombre);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmm");
        await _audit.LogAsync("ocupacion_exporta", $"{formato.ToLowerInvariant()}: {Describe(filtro)}");

        if (string.Equals(formato, "csv", StringComparison.OrdinalIgnoreCase))
        {
            Response.ContentType = "text/csv; charset=utf-8";
            Response.Headers.ContentDisposition = $"attachment; filename=\"Ocupacion Edificios {stamp}.csv\"";
            await _service.WriteCsvAsync(filtro, Response.Body, ct);
            return new EmptyResult();
        }

        if (!string.Equals(formato, "xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(formato, "excel", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Formato no soportado (use xlsx o csv)." });
        }

        var path = await Task.Run(() => _service.WriteXlsxToTempFile(filtro), ct);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Ocupacion Edificios {stamp}.xlsx");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("upload")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<IActionResult> Upload([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { message = "No se recibieron archivos." });
        }

        var resultados = new List<OcupacionCargaResultado>();
        foreach (var file in files)
        {
            var nombre = Path.GetFileName(string.IsNullOrWhiteSpace(file.FileName) ? "archivo.xlsx" : file.FileName);
            if (!nombre.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !nombre.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
            {
                resultados.Add(new OcupacionCargaResultado(nombre, "error", Mensaje: "Solo se aceptan archivos .xlsx/.xlsm"));
                continue;
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var res = await _service.CargarAsync(nombre, ms.ToArray(), ct);
            resultados.Add(res);
            await _audit.LogAsync("ocupacion_carga",
                $"{nombre}: {res.Estado}" + (res.Estado == "cargado" ? $", {res.FilasInsertadas} filas nuevas" : ""));
        }
        return Ok(new { resultados });
    }

    [HttpGet("archivos")]
    public IActionResult Archivos()
    {
        var items = _service.GetArchivos();
        return Ok(new { items, total = items.Count });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("archivos/{id:long}")]
    public async Task<IActionResult> EliminarArchivo(long id, CancellationToken ct)
    {
        var res = await _service.EliminarArchivoAsync(id, ct);
        if (res == null) return NotFound(new { message = "Archivo no encontrado." });
        await _audit.LogAsync("ocupacion_borra", $"{res.Value.Nombre}: {res.Value.Borrados} registros");
        return Ok(new
        {
            id,
            nombre = res.Value.Nombre,
            registrosEliminados = res.Value.Borrados,
            message = $"Se eliminó '{res.Value.Nombre}' y sus {res.Value.Borrados.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO"))} registros."
        });
    }
}
