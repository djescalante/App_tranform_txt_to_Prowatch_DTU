using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsuariosRetirados.Server.Data;
using UsuariosRetirados.Server.DTOs;
using UsuariosRetirados.Server.Models;

namespace UsuariosRetirados.Server.Controllers;

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
        var users = await _db.Users
            .OrderBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt,
                u.Username.ToLower() == Models.User.PrincipalAdminUsername))
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

        if (req.Password.Length < 8)
        {
            return BadRequest(new { message = "La contraseña debe tener al menos 8 caracteres." });
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
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt, null));
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

        if (!string.IsNullOrWhiteSpace(req.Password) && req.Password.Length < 8)
        {
            return BadRequest(new { message = "La contraseña debe tener al menos 8 caracteres." });
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
        }

        await _db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt, user.LastLoginAt, IsPrincipal(user)));
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
