using System.ComponentModel.DataAnnotations;

namespace UsuariosRetirados.Server.Models;

public class AppConfig
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
