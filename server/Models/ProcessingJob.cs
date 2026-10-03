using System.ComponentModel.DataAnnotations;

namespace UsuariosRetirados.Server.Models;

public class ProcessingJob
{
    [Key]
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string CreatedByUsername { get; set; } = string.Empty;

    [MaxLength(255)]
    public string SourceFileName { get; set; } = string.Empty;

    public DateTime? SourceFileDate { get; set; }

    [MaxLength(20)]
    public string EventDate { get; set; } = string.Empty; // dd/MM/yyyy

    public string SelectedStates { get; set; } = string.Empty; // JSON or comma-separated

    public string SelectedSocieties { get; set; } = string.Empty; // JSON or comma-separated

    public int TotalMatchedRows { get; set; }

    public long ExecutionDurationMs { get; set; }

    [MaxLength(255)]
    public string? DtuFileName { get; set; }

    [MaxLength(255)]
    public string? XlsxFileName { get; set; }

    [MaxLength(255)]
    public string? TsvFileName { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Completed"; // Completed, Failed

    public string? ErrorMessage { get; set; }
}
