using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsuariosRetirados.Server.Data;
using UsuariosRetirados.Server.DTOs;

namespace UsuariosRetirados.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly AppDbContext _db;

    public JobsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<JobDto>>> GetJobs([FromQuery] int limit = 50)
    {
        var jobs = await _db.ProcessingJobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit > 0 ? limit : 50)
            .Select(j => new JobDto(
                j.Id,
                j.CreatedAt,
                j.CreatedByUsername,
                j.SourceFileName,
                j.EventDate,
                j.SelectedStates,
                j.SelectedSocieties,
                j.TotalMatchedRows,
                j.ExecutionDurationMs,
                j.DtuFileName,
                j.XlsxFileName,
                j.TsvFileName,
                j.Status,
                j.ErrorMessage
            ))
            .ToListAsync();

        return Ok(jobs);
    }

    [HttpGet("{id}/download/{format}")]
    public async Task<IActionResult> DownloadFile(int id, string format)
    {
        var job = await _db.ProcessingJobs.FindAsync(id);
        if (job == null) return NotFound(new { message = "Registro de proceso no encontrado." });

        string? fileName = format.ToLower() switch
        {
            "dtu" or "txt" => job.DtuFileName,
            "xlsx" or "excel" => job.XlsxFileName,
            "tsv" => job.TsvFileName,
            _ => null
        };

        if (string.IsNullOrEmpty(fileName))
        {
            return NotFound(new { message = $"No se generó archivo en formato '{format}' para este proceso." });
        }

        var cfg = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == "OutputDir");
        string outDir = cfg?.Value ?? @"E:\CarpetaTrabajoIA\empleados\UsuariosRetiradosDTU\salidas";
        string filePath = Path.Combine(outDir, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            var altPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "UsuariosRetiradosDTU", "salidas", fileName);
            if (System.IO.File.Exists(altPath))
            {
                filePath = altPath;
            }
            else
            {
                return NotFound(new { message = $"El archivo físico '{fileName}' ya no existe en el disco." });
            }
        }

        string contentType = format.ToLower() switch
        {
            "dtu" or "txt" => "text/plain; charset=utf-8",
            "xlsx" or "excel" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "tsv" => "text/tab-separated-values; charset=utf-8",
            _ => "application/octet-stream"
        };

        return PhysicalFile(filePath, contentType, fileName);
    }
}
