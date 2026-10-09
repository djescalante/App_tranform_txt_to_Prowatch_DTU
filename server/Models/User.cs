using System.ComponentModel.DataAnnotations;

namespace PWExtendedApp.Server.Models;

public class User
{
    /// <summary>
    /// Cuenta de administrador principal (la del seed). No se puede eliminar,
    /// desactivar ni quitarle el rol Admin desde la app.
    /// </summary>
    public const string PrincipalAdminUsername = "admin";

    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = "Operator"; // "Admin" or "Operator"

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Contraseña temporal (seed o asignada por un admin): mientras sea true, la API solo
    /// permite cambiarla.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Intentos fallidos consecutivos (se reinicia al ingresar o al bloquear).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Bloqueo temporal por intentos fallidos (UTC).</summary>
    public DateTime? LockoutUntil { get; set; }
}
