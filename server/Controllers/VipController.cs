using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsuariosRetirados.Server.Data;
using UsuariosRetirados.Server.DTOs;
using UsuariosRetirados.Server.Models;

namespace UsuariosRetirados.Server.Controllers;

/// <summary>
/// Lista VIP: cédulas que nunca deben exportarse en los insumos de bloqueo.
/// Lectura para cualquier usuario autenticado; alta/edición/baja solo Admin.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class VipController : ControllerBase
{
    private readonly AppDbContext _db;

    public VipController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<VipEmployeeDto>>> GetAll()
    {
        var list = await _db.VipEmployees
            .OrderBy(v => v.FullName)
            .Select(v => new VipEmployeeDto(v.Id, v.Cedula, v.FullName, v.CreatedByUsername, v.CreatedAt, v.UpdatedAt))
            .ToListAsync();

        return Ok(list);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<VipEmployeeDto>> Create([FromBody] CreateVipRequest req)
    {
        var (ok, error, cedula, fullName) = Validate(req.Cedula, req.FullName);
        if (!ok) return BadRequest(new { message = error });

        if (await _db.VipEmployees.AnyAsync(v => v.Cedula == cedula))
        {
            return BadRequest(new { message = $"La cédula '{cedula}' ya está en la lista VIP." });
        }

        var vip = new VipEmployee
        {
            Cedula = cedula,
            FullName = fullName,
            CreatedByUsername = User.Identity?.Name,
            CreatedAt = DateTime.UtcNow
        };

        _db.VipEmployees.Add(vip);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { id = vip.Id },
            new VipEmployeeDto(vip.Id, vip.Cedula, vip.FullName, vip.CreatedByUsername, vip.CreatedAt, vip.UpdatedAt));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<VipEmployeeDto>> Update(int id, [FromBody] UpdateVipRequest req)
    {
        var vip = await _db.VipEmployees.FindAsync(id);
        if (vip == null) return NotFound(new { message = "Registro VIP no encontrado." });

        var (ok, error, cedula, fullName) = Validate(req.Cedula, req.FullName);
        if (!ok) return BadRequest(new { message = error });

        if (await _db.VipEmployees.AnyAsync(v => v.Cedula == cedula && v.Id != id))
        {
            return BadRequest(new { message = $"La cédula '{cedula}' ya está en la lista VIP." });
        }

        vip.Cedula = cedula;
        vip.FullName = fullName;
        vip.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new VipEmployeeDto(vip.Id, vip.Cedula, vip.FullName, vip.CreatedByUsername, vip.CreatedAt, vip.UpdatedAt));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var vip = await _db.VipEmployees.FindAsync(id);
        if (vip == null) return NotFound(new { message = "Registro VIP no encontrado." });

        _db.VipEmployees.Remove(vip);
        await _db.SaveChangesAsync();

        return Ok(new { message = $"Se quitó '{vip.Cedula}' de la lista VIP." });
    }

    private static (bool Ok, string? Error, string Cedula, string FullName) Validate(string? cedula, string? fullName)
    {
        var c = (cedula ?? string.Empty).Trim();
        var n = (fullName ?? string.Empty).Trim();

        if (c.Length < 3 || c.Length > 30)
        {
            return (false, "La cédula debe tener entre 3 y 30 caracteres.", c, n);
        }

        if (c.Contains(' '))
        {
            return (false, "La cédula no debe contener espacios.", c, n);
        }

        if (n.Length < 2 || n.Length > 150)
        {
            return (false, "El nombre debe tener entre 2 y 150 caracteres.", c, n);
        }

        return (true, null, c, n);
    }
}
