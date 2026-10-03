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
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt))
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

        user.FullName = req.FullName.Trim();
        user.Role = string.Equals(req.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Operator";
        user.IsActive = req.IsActive;

        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        }

        await _db.SaveChangesAsync();
        return Ok(new UserDto(user.Id, user.Username, user.FullName, user.Role, user.IsActive, user.CreatedAt, user.LastLoginAt));
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
            var item = await _db.AppConfigs.FirstOrDefaultAsync(c => c.Key == key);
            if (item != null)
            {
                item.Value = value;
                item.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.AppConfigs.Add(new AppConfig { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Configuración actualizada correctamente." });
    }
}
