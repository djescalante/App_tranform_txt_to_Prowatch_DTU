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
    private readonly IWebHostEnvironment _env;

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
        IWebHostEnvironment env)
    {
        _db = db;
        _csvEngine = csvEngine;
        _exportService = exportService;
        _schemaValidator = schemaValidator;
        _env = env;
    }

    private async Task<string> GetInputPathAsync()
    {
        var cfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "InputPath");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Value)) return cfg.Value;
        return @"E:\CarpetaTrabajoIA\empleados\Empleados.txt";
    }

    private async Task<string> GetOutputDirAsync()
    {
        var cfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "OutputDir");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Value)) return cfg.Value;
        return @"E:\CarpetaTrabajoIA\empleados\UsuariosRetiradosDTU\salidas";
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

        string schemaPath = Path.Combine(_env.ContentRootPath, "..", "UsuariosRetiradosDTU", "app", "estructura.json");
        var schemaRes = header != null
            ? _schemaValidator.ValidateHeader(header, schemaPath)
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

        var result = _csvEngine.FilterRows(
            path,
            req.FechaEvento,
            req.Sociedades,
            req.Estados,
            maxCollect: req.Limit > 0 ? req.Limit : 100
        );

        return Ok(new PreviewResponse(result.MatchedCount, result.SampleRows, result.ElapsedMs));
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

        // Run full filtering
        var result = _csvEngine.FilterRows(
            path,
            req.FechaEvento,
            req.Sociedades,
            req.Estados,
            maxCollect: 0 // Collect all matches
        );

        // Generate files
        var exportRes = _exportService.GenerateOutputs(
            result.SampleRows,
            outDir,
            req.FechaEvento,
            emitXlsx: req.EmitXlsx,
            emitTsv: req.EmitTsv,
            emitDtu: req.EmitDtu
        );

        // Record job in SQLite database
        var username = User.Identity?.Name ?? "Sistema";
        var job = new ProcessingJob
        {
            CreatedAt = DateTime.UtcNow,
            CreatedByUsername = username,
            SourceFileName = fi.Name,
            SourceFileDate = fi.LastWriteTime,
            EventDate = req.FechaEvento,
            SelectedStates = string.Join(", ", req.Estados),
            SelectedSocieties = string.Join(", ", req.Sociedades),
            TotalMatchedRows = result.MatchedCount,
            ExecutionDurationMs = result.ElapsedMs,
            DtuFileName = exportRes.DtuFileName,
            XlsxFileName = exportRes.XlsxFileName,
            TsvFileName = exportRes.TsvFileName,
            Status = "Completed"
        };

        _db.ProcessingJobs.Add(job);
        await _db.SaveChangesAsync();

        return Ok(new ProcessResponse(
            JobId: job.Id,
            Success: true,
            TotalCoinciden: result.MatchedCount,
            ElapsedMs: result.ElapsedMs,
            FilesGenerated: exportRes.GeneratedFilePaths.Select(Path.GetFileName).ToList()!,
            Message: $"Proceso completado exitosamente con {result.MatchedCount} registros generados en {result.ElapsedMs} ms."
        ));
    }
}
