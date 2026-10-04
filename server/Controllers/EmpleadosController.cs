using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsuariosRetirados.Server.Data;
using UsuariosRetirados.Server.DTOs;
using UsuariosRetirados.Server.Models;
using UsuariosRetirados.Server.Services;

namespace UsuariosRetirados.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EmpleadosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICsvStreamingEngine _csvEngine;
    private readonly IExportService _exportService;
    private readonly ISchemaValidator _schemaValidator;
    private readonly IScanCache _scanCache;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<EmpleadosController> _logger;

    private static readonly List<string> SociedadesFijas =
    [
        "BANCOLOMBIA",
        "NEQUI SA",
        "VALORES BANCOLOMBIA",
        "BANCA DE INVERSION BANCOLOMBIA"
    ];

    private static readonly List<string> EstadosDisponibles =
    [
        "Terminated",
        "Con terminación de contrato",
        "ReportNo-Show"
    ];

    public EmpleadosController(
        AppDbContext db,
        ICsvStreamingEngine csvEngine,
        IExportService exportService,
        ISchemaValidator schemaValidator,
        IScanCache scanCache,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<EmpleadosController> logger)
    {
        _db = db;
        _csvEngine = csvEngine;
        _exportService = exportService;
        _schemaValidator = schemaValidator;
        _scanCache = scanCache;
        _env = env;
        _config = config;
        _logger = logger;
    }

    private async Task<string> GetInputPathAsync()
    {
        var cfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "InputPath");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Value)) return cfg.Value;
        return _config["AppPaths:InputPath"] ?? string.Empty;
    }

    private async Task<string> GetOutputDirAsync()
    {
        var cfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "OutputDir");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Value)) return cfg.Value;
        return _config["AppPaths:OutputDir"] ?? AppPaths.OutputRelative;
    }

    private static string BuildScanKey(string path, string? fechaEvento, IEnumerable<string>? sociedades, IEnumerable<string>? estados)
    {
        string Normalize(IEnumerable<string>? values) =>
            values == null
                ? string.Empty
                : string.Join(",",
                    values.Select(v => v.Trim().ToUpperInvariant())
                          .Where(v => v.Length > 0)
                          .OrderBy(v => v, StringComparer.Ordinal));

        var fi = new FileInfo(path);
        long length = fi.Exists ? fi.Length : 0;
        long ticks = fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0;

        return $"scan|{path.ToLowerInvariant()}|{length}|{ticks}|{(fechaEvento ?? string.Empty).Trim()}|{Normalize(sociedades)}|{Normalize(estados)}";
    }

    private FilterResult GetOrScan(string path, string? fechaEvento, IEnumerable<string>? sociedades, IEnumerable<string>? estados)
    {
        var key = BuildScanKey(path, fechaEvento, sociedades, estados);
        if (_scanCache.TryGet(key, out var cached))
        {
            return cached;
        }

        var result = _csvEngine.FilterRows(path, fechaEvento, sociedades, estados, maxCollect: 0);
        _scanCache.Set(key, result);
        return result;
    }

    private async Task<Dictionary<string, string>> GetVipMapAsync()
    {
        var list = await _db.VipEmployees.AsNoTracking()
            .Select(v => new { v.Cedula, v.FullName })
            .ToListAsync();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in list)
        {
            var key = v.Cedula.Trim();
            if (key.Length > 0) map[key] = v.FullName;
        }
        return map;
    }

    /// <summary>
    /// Separa las coincidencias: las cédulas VIP se omiten (alertadas) y el resto se exporta.
    /// El cache de escaneo guarda filas crudas, así los cambios de la lista aplican al instante.
    /// </summary>
    private static (List<EmpleadoRowDto> Kept, List<VipOmittedRow> Omitted) SplitVip(
        List<EmpleadoRowDto> rows, Dictionary<string, string> vipMap)
    {
        var kept = new List<EmpleadoRowDto>();
        var omitted = new List<VipOmittedRow>();

        foreach (var row in rows)
        {
            var doc = row.Documento.Trim();
            if (vipMap.TryGetValue(doc, out var name))
            {
                omitted.Add(new VipOmittedRow(doc, name));
            }
            else
            {
                kept.Add(row);
            }
        }

        return (kept, omitted);
    }

    [HttpGet("info")]
    public async Task<ActionResult<PadronInfoResponse>> GetPadronInfo()
    {
        var path = await GetInputPathAsync();
        bool exists = System.IO.File.Exists(path);

        if (!exists)
        {
            return Ok(new PadronInfoResponse(
                Exists: false,
                Path: path,
                SizeBytes: 0,
                SizeFormatted: "0 B",
                LastModified: null,
                SuggestedDate: DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy"),
                TotalColumns: 0,
                SchemaValid: false,
                SchemaIssues: ["El archivo padrón no existe en la ruta configurada."]
            ));
        }

        var fi = new FileInfo(path);
        string suggestedDate = fi.LastWriteTime.Date.AddDays(-1).ToString("dd/MM/yyyy");

        string sizeFormatted = fi.Length switch
        {
            >= 1024 * 1024 * 1024 => $"{fi.Length / (1024.0 * 1024 * 1024):F2} GB",
            >= 1024 * 1024 => $"{fi.Length / (1024.0 * 1024):F1} MB",
            >= 1024 => $"{fi.Length / 1024.0:F1} KB",
            _ => $"{fi.Length} B"
        };

        var header = _csvEngine.ReadHeader(path);
        int totalCols = header?.Length ?? 0;

        var estructuraCfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "EstructuraPath");
        string? schemaPath = AppPaths.FindEstructuraJson(_env.ContentRootPath, estructuraCfg?.Value);

        var schemaRes = header != null
            ? _schemaValidator.ValidateHeader(header, schemaPath ?? string.Empty)
            : new SchemaValidationResult(false, true, ["No se pudo leer el encabezado."], [], [], 0, 0);

        var issues = new List<string>();
        if (schemaRes.MissingRequired)
        {
            issues.Add($"Faltan columnas obligatorias: {string.Join(", ", schemaRes.MissingRequiredColumns)}");
        }
        if (schemaRes.MissingColumns.Count > 0)
        {
            issues.Add($"Columnas faltantes respecto al esquema base: {schemaRes.MissingColumns.Count}");
        }
        if (schemaRes.ExtraColumns.Count > 0)
        {
            issues.Add($"Columnas adicionales no reconocidas: {schemaRes.ExtraColumns.Count}");
        }

        return Ok(new PadronInfoResponse(
            Exists: true,
            Path: path,
            SizeBytes: fi.Length,
            SizeFormatted: sizeFormatted,
            LastModified: fi.LastWriteTime,
            SuggestedDate: suggestedDate,
            TotalColumns: totalCols,
            SchemaValid: schemaRes.IsValid,
            SchemaIssues: issues
        ));
    }

    [HttpGet("options")]
    public async Task<ActionResult<FilterOptionsResponse>> GetOptions()
    {
        var path = await GetInputPathAsync();
        string suggestedDate = DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy");
        if (System.IO.File.Exists(path))
        {
            suggestedDate = new FileInfo(path).LastWriteTime.Date.AddDays(-1).ToString("dd/MM/yyyy");
        }

        return Ok(new FilterOptionsResponse(
            EstadosDisponibles: EstadosDisponibles,
            SociedadesFijas: SociedadesFijas,
            DefaultFechaEvento: suggestedDate
        ));
    }

    [HttpPost("preview")]
    public async Task<ActionResult<PreviewResponse>> Preview([FromBody] PreviewRequest req)
    {
        var path = await GetInputPathAsync();
        if (!System.IO.File.Exists(path))
        {
            return BadRequest(new { message = $"El archivo de empleados no fue encontrado en: {path}" });
        }

        try
        {
            var result = GetOrScan(path, req.FechaEvento, req.Sociedades, req.Estados);
            var vipMap = await GetVipMapAsync();
            var (kept, omitted) = SplitVip(result.Rows, vipMap);
            int limit = req.Limit > 0 ? req.Limit : 100;

            return Ok(new PreviewResponse(
                kept.Count,
                kept.Take(limit).ToList(),
                result.ElapsedMs,
                omitted.Count,
                omitted
            ));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Fallo la vista previa del padrón {Path}", path);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"Error al leer el padrón: {ex.Message}" });
        }
    }

    [HttpPost("process")]
    public async Task<ActionResult<ProcessResponse>> Process([FromBody] ProcessRequest req)
    {
        var path = await GetInputPathAsync();
        var outDir = await GetOutputDirAsync();

        if (!System.IO.File.Exists(path))
        {
            return BadRequest(new { message = $"El archivo de empleados no fue encontrado en: {path}" });
        }

        if (string.IsNullOrWhiteSpace(req.FechaEvento))
        {
            return BadRequest(new { message = "La fecha del evento es obligatoria." });
        }

        if (req.Sociedades == null || req.Sociedades.Count == 0)
        {
            return BadRequest(new { message = "Debe seleccionar al menos una sociedad." });
        }

        if (req.Estados == null || req.Estados.Count == 0)
        {
            return BadRequest(new { message = "Debe seleccionar al menos un estado." });
        }

        var fi = new FileInfo(path);
        var username = User.Identity?.Name ?? "Sistema";

        try
        {
            var result = GetOrScan(path, req.FechaEvento, req.Sociedades, req.Estados);
            var vipMap = await GetVipMapAsync();
            var (kept, omitted) = SplitVip(result.Rows, vipMap);

            var exportRes = _exportService.GenerateOutputs(
                kept,
                outDir,
                req.FechaEvento,
                emitXlsx: req.EmitXlsx,
                emitTsv: req.EmitTsv,
                emitDtu: req.EmitDtu
            );

            var job = new ProcessingJob
            {
                CreatedAt = DateTime.UtcNow,
                CreatedByUsername = username,
                SourceFileName = fi.Name,
                SourceFileDate = fi.LastWriteTime,
                EventDate = req.FechaEvento,
                SelectedStates = string.Join(", ", req.Estados),
                SelectedSocieties = string.Join(", ", req.Sociedades),
                TotalMatchedRows = kept.Count,
                VipOmittedCount = omitted.Count,
                VipOmittedDetails = omitted.Count > 0 ? string.Join(", ", omitted.Select(o => o.Cedula)) : null,
                ExecutionDurationMs = result.ElapsedMs,
                DtuFileName = exportRes.DtuFileName,
                XlsxFileName = exportRes.XlsxFileName,
                TsvFileName = exportRes.TsvFileName,
                Status = "Completed"
            };

            _db.ProcessingJobs.Add(job);
            await _db.SaveChangesAsync();

            var vipNote = omitted.Count > 0 ? $" Se omitieron {omitted.Count} cédulas de la lista VIP." : string.Empty;

            return Ok(new ProcessResponse(
                JobId: job.Id,
                Success: true,
                TotalCoinciden: kept.Count,
                ElapsedMs: result.ElapsedMs,
                FilesGenerated: exportRes.GeneratedFilePaths.Select(Path.GetFileName).ToList()!,
                Message: $"Proceso completado exitosamente con {kept.Count} registros generados en {result.ElapsedMs} ms.{vipNote}",
                VipOmittedCount: omitted.Count
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo el proceso de exportación para {Path} -> {OutDir}", path, outDir);

            _db.ProcessingJobs.Add(new ProcessingJob
            {
                CreatedAt = DateTime.UtcNow,
                CreatedByUsername = username,
                SourceFileName = fi.Name,
                SourceFileDate = fi.Exists ? fi.LastWriteTime : null,
                EventDate = req.FechaEvento,
                SelectedStates = string.Join(", ", req.Estados),
                SelectedSocieties = string.Join(", ", req.Sociedades),
                TotalMatchedRows = 0,
                ExecutionDurationMs = 0,
                Status = "Failed",
                ErrorMessage = ex.Message
            });
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"Error al procesar el padrón: {ex.Message}" });
        }
    }
}
