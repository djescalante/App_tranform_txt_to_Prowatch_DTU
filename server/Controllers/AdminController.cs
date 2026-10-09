using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PWExtendedApp.Server.Data;
using PWExtendedApp.Server.DTOs;
using PWExtendedApp.Server.Models;
using PWExtendedApp.Server.Services;

namespace PWExtendedApp.Server.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var now = DateTime.UtcNow;
        var users = await _db.Users
            .OrderBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt,
                u.Username.ToLower() == Models.User.PrincipalAdminUsername,
                u.MustChangePassword,
                u.LockoutUntil != null && u.LockoutUntil > now))
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "Nombre de usuario y contraseña son requeridos." });
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(req.Username.Trim(), @"^[A-Za-z0-9._-]{3,50}$"))
        {
            return BadRequest(new { message = "El usuario debe tener entre 3 y 50 caracteres (letras, números, punto, guion o guion bajo)." });
        }

        var pwdError = PasswordPolicy.Validate(req.Password, req.Username.Trim());
        if (pwdError != null)
        {
            return BadRequest(new { message = pwdError });
        }

        if (string.IsNullOrWhiteSpace(req.FullName))
        {
            return BadRequest(new { message = "El nombre completo es requerido." });
        }

        var normalizedUsername = req.Username.Trim().ToLower();
        if (await _db.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername))
        {
            return BadRequest(new { message = $"El usuario '{req.Username}' ya existe." });
        }

        var user = new User
        {
            Username = req.Username.Trim(),
            FullName = req.FullName.Trim(),
            Role = string.Equals(req.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Operator",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            IsActive = true,
            // La asigna el admin: es temporal y el usuario la cambia al ingresar.
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt, null,
            MustChangePassword: true));
    }

    [HttpPut("users/{id}")]
    public async Task<ActionResult<UserDto>> UpdateUser(int id, [FromBody] UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        if (string.IsNullOrWhiteSpace(req.FullName))
        {
            return BadRequest(new { message = "El nombre completo es requerido." });
        }

        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            var pwdError = PasswordPolicy.Validate(req.Password, user.Username);
            if (pwdError != null)
            {
                return BadRequest(new { message = pwdError });
            }
        }

        var newRole = string.Equals(req.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Operator";
        if (IsPrincipal(user) && (newRole != "Admin" || !req.IsActive))
        {
            return BadRequest(new { message = "El administrador principal debe seguir activo y con rol Admin." });
        }

        user.FullName = req.FullName.Trim();
        user.Role = newRole;
        user.IsActive = req.IsActive;

        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            // Clave asignada por otro admin = temporal; si el admin cambia la suya, no.
            user.MustChangePassword = User.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id.ToString();
        }

        // Una clave nueva o el desbloqueo explícito liberan la cuenta.
        if (req.Unlock || !string.IsNullOrWhiteSpace(req.Password))
        {
            user.FailedLoginCount = 0;
            user.LockoutUntil = null;
        }

        await _db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt, user.LastLoginAt,
            IsPrincipal(user), user.MustChangePassword, user.LockoutUntil > DateTime.UtcNow));
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        if (IsPrincipal(user))
        {
            return BadRequest(new { message = "El administrador principal no se puede eliminar." });
        }

        // Quien llama es un admin activo (lo valida OnTokenValidated), así que impedir
        // el auto-borrado garantiza que siempre quede al menos un administrador.
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString())
        {
            return BadRequest(new { message = "No puede eliminar su propio usuario." });
        }

        // El historial guarda el nombre de usuario como texto, así que los procesos
        // que hizo este usuario se conservan.
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = $"Se eliminó el usuario '{user.Username}'." });
    }

    private static bool IsPrincipal(Models.User user) =>
        string.Equals(user.Username, Models.User.PrincipalAdminUsername, StringComparison.OrdinalIgnoreCase);

    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit([FromQuery] string? q, [FromQuery] int limit = 300)
    {
        var query = _db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(a => EF.Functions.Like(a.Username, term) || EF.Functions.Like(a.Action, term) ||
                                     EF.Functions.Like(a.Detail, term));
        }
        var items = await query.OrderByDescending(a => a.Id).Take(Math.Clamp(limit, 1, 1000)).ToListAsync();
        return Ok(items);
    }

    [HttpGet("backups")]
    public ActionResult<BackupStatus> GetBackups([FromServices] BackupService backup) => Ok(backup.GetStatus());

    [HttpPost("backups")]
    public async Task<ActionResult<BackupRun>> RunBackup([FromServices] BackupService backup, CancellationToken ct)
    {
        var run = await backup.RunAsync(ct);
        return run.Success ? Ok(run) : StatusCode(StatusCodes.Status500InternalServerError, new { message = run.Message });
    }

    [HttpPost("jobs/clear")]
    public async Task<IActionResult> ClearJobs()
    {
        var deleted = await _db.ProcessingJobs.ExecuteDeleteAsync();
        return Ok(new { message = $"Se eliminaron {deleted} procesos del historial.", deleted });
    }

    [HttpGet("config")]
    public async Task<ActionResult<Dictionary<string, string>>> GetConfig()
    {
        var configs = await _db.AppConfigs.ToDictionaryAsync(c => c.Key, c => c.Value);
        return Ok(configs);
    }

    [HttpPost("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] Dictionary<string, string> newConfigs)
    {
        foreach (var (key, value) in newConfigs)
        {
            if ((key == "InputPath" || key == "OutputDir") && string.IsNullOrWhiteSpace(value))
            {
                return BadRequest(new { message = $"La ruta '{key}' no puede estar vacía." });
            }

            var item = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == key);
            if (item != null)
            {
                item.Value = value.Trim();
                item.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.AppConfigs.Add(new AppConfig { Key = key, Value = value.Trim(), UpdatedAt = DateTime.UtcNow });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Configuración actualizada correctamente." });
    }
}
