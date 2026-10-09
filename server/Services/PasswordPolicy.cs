namespace PWExtendedApp.Server.Services;

/// <summary>Reglas de contraseña y de bloqueo por intentos fallidos.</summary>
public static class PasswordPolicy
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Contraseñas del seed: nunca se aceptan como contraseña definitiva.</summary>
    public static readonly string[] SeedPasswords = ["Admin123!", "Operador123!"];

    public const string Description =
        "Mínimo 8 caracteres, con letras y números, distinta del usuario y de las contraseñas iniciales.";

    /// <summary>Devuelve el motivo por el que la contraseña no es válida, o null si cumple.</summary>
    public static string? Validate(string? password, string username)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return "La contraseña debe tener al menos 8 caracteres.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "La contraseña debe combinar letras y números.";
        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            return "La contraseña no puede ser igual al usuario.";
        if (SeedPasswords.Contains(password))
            return "No se puede usar una contraseña inicial del sistema.";
        return null;
    }
}
