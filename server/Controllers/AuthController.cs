using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PWExtendedApp.Server.Data;
using PWExtendedApp.Server.DTOs;
using PWExtendedApp.Server.Models;
using PWExtendedApp.Server.Services;

namespace PWExtendedApp.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // Mismo mensaje para usuario inexistente, inactivo o clave errada: no revela qué cuentas existen.
    private const string InvalidCredentials = "Usuario o contraseña incorrectos.";

    private readonly AppDbContext _db;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthController> _logger;
    private readonly AuditService _audit;

    public AuthController(AppDbContext db, IJwtService jwtService, ILogger<AuthController> logger, AuditService audit)
    {
        _db = db;
        _jwtService = jwtService;
        _logger = logger;
        _audit = audit;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "Usuario y contraseña requeridos." });
        }

        var username = req.Username.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username);
        if (user == null || !user.IsActive)
        {
            await _audit.LogAsync("ingreso_fallido", user == null ? "Usuario inexistente" : "Usuario inactivo", username);
            return Unauthorized(new { message = InvalidCredentials });
        }

        var now = DateTime.UtcNow;
        if (user.LockoutUntil is { } until && until > now)
        {
            await _audit.LogAsync("ingreso_fallido", "Cuenta bloqueada", user.Username);
            return Unauthorized(new { message = LockedMessage(until - now) });
        }

        if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= PasswordPolicy.MaxFailedAttempts)
            {
                user.FailedLoginCount = 0;
                user.LockoutUntil = now.Add(PasswordPolicy.LockoutDuration);
                await _db.SaveChangesAsync();
                _logger.LogWarning("Cuenta {User} bloqueada por intentos fallidos desde {Ip}",
                    user.Username, HttpContext.Connection.RemoteIpAddress);
                await _audit.LogAsync("cuenta_bloqueada",
                    $"{PasswordPolicy.MaxFailedAttempts} intentos fallidos; bloqueada {PasswordPolicy.LockoutDuration.TotalMinutes:0} min",
                    user.Username);
                return Unauthorized(new { message = LockedMessage(PasswordPolicy.LockoutDuration) });
            }
            await _db.SaveChangesAsync();
            await _audit.LogAsync("ingreso_fallido", $"Contraseña incorrecta (intento {user.FailedLoginCount})", user.Username);
            return Unauthorized(new { message = InvalidCredentials });
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("ingreso", user.MustChangePassword ? "Con contraseña temporal" : "Correcto", user.Username);
        var token = _jwtService.GenerateToken(user);
        return Ok(new LoginResponse(token, user.Username, user.FullName, user.Role, user.MustChangePassword));
    }

    private static string LockedMessage(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"Cuenta bloqueada por {PasswordPolicy.MaxFailedAttempts} intentos fallidos. " +
               $"Intente de nuevo en {minutes} min o pida a un administrador que la desbloquee.";
    }

    private async Task<User?> CurrentUserAsync()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await _db.Users.FindAsync(id)
            : null;
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe()
    {
        var user = await CurrentUserAsync();
        if (user == null) return Unauthorized();

        return Ok(new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt,
            user.LastLoginAt, MustChangePassword: user.MustChangePassword));
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var user = await CurrentUserAsync();
        if (user == null) return Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword ?? string.Empty, user.PasswordHash))
        {
            return BadRequest(new { message = "La contraseña actual no es correcta." });
        }

        if (req.NewPassword == req.CurrentPassword)
        {
            return BadRequest(new { message = "La nueva contraseña debe ser diferente a la actual." });
        }

        var error = PasswordPolicy.Validate(req.NewPassword, user.Username);
        if (error != null)
        {
            return BadRequest(new { message = error });
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("cambio_contrasena", "El usuario cambió su contraseña");

        return Ok(new { message = "Contraseña actualizada." });
    }
}
