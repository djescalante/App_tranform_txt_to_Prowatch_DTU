using System.ComponentModel.DataAnnotations;

namespace PWExtendedApp.Server.Models;

/// <summary>
/// Bitácora de auditoría: accesos y acciones sobre datos personales (consultas y
/// exportaciones de Ocupación, descargas DTU, cargas/borrados e ingresos).
/// </summary>
public class AuditEntry
{
    [Key]
    public int Id { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Detail { get; set; } = string.Empty;

    [MaxLength(45)]
    public string? Ip { get; set; }
}
