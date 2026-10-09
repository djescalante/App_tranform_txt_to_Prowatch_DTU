using System.ComponentModel.DataAnnotations;

namespace PWExtendedApp.Server.Models;

/// <summary>
/// Persona protegida: su cédula nunca debe exportarse en los insumos de bloqueo.
/// </summary>
public class VipEmployee
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Cedula { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? CreatedByUsername { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
