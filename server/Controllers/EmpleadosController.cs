using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PWExtendedApp.Server.Data;
using PWExtendedApp.Server.DTOs;
using PWExtendedApp.Server.Models;
using PWExtendedApp.Server.Services;

namespace PWExtendedApp.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class EmpleadosController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICsvStreamingEngine _csvEngine;
    private readonly IExportService _exportService;
    private readonly ISchemaValidator _schemaValidator;
    private readonly PadronCache _padron;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<EmpleadosController> _logger;

    private static readonly SemaphoreSlim ProcessLock = new(1, 1);

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
        PadronCache padron,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<EmpleadosController> logger)
    {
        _db = db;
        _csvEngine = csvEngine;
        _exportService = exportService;
        _schemaValidator = schemaValidator;
        _padron = padron;
        _env = env;
        _config = config;
        _logger = logger;
    }

    private Task<string> GetInputPathAsync() => PadronCache.ResolveInputPathAsync(_db, _config);

    private async Task<string> GetOutputDirAsync()
    {
        var cfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "OutputDir");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Value)) return cfg.Value;
        return _config["AppPaths:OutputDir"] ?? AppPaths.OutputRelative;
    }

    /// <summary>Filtra el padrón en memoria (se lee del disco solo si el archivo cambió).</summary>
    private FilterResult GetOrScan(string path, string? fechaEvento, IEnumerable<string>? sociedades, IEnumerable<string>? estados)
    {
        var padron = _padron.Get(path);
        return _csvEngine.Filter(padron, fechaEvento, sociedades, estados);
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
            var code = ErrorReference.NewCode();
            _logger.LogError(ex, "Fallo la vista previa del padrón {Path} (ref {Ref})", path, code);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = ErrorReference.Message("No se pudo leer el padrón.", code) });
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

        // Un proceso a la vez: dos procesos de la misma fecha escribirían los mismos archivos.
        await ProcessLock.WaitAsync(HttpContext.RequestAborted);
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
            var code = ErrorReference.NewCode();
            _logger.LogError(ex, "Fallo el proceso de exportación para {Path} -> {OutDir} (ref {Ref})", path, outDir, code);

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
                ErrorMessage = $"{ex.Message} (ref {code})"
            });
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = ErrorReference.Message("No se pudo completar el proceso.", code) });
        }
        finally
        {
            ProcessLock.Release();
        }
    }
}
