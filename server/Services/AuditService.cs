using PWExtendedApp.Server.Data;
using PWExtendedApp.Server.Models;

namespace PWExtendedApp.Server.Services;

/// <summary>Escribe en la bitácora de auditoría (tabla AuditLog de la base de usuarios).</summary>
public class AuditService
{
    public const int RetentionDays = 365;

    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger)
    {
        _db = db;
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Registra una acción. Si no se indica usuario se toma el de la sesión. Un fallo al
    /// auditar se registra en el log pero no interrumpe la operación del usuario.
    /// </summary>
    public async Task LogAsync(string action, string detail, string? username = null)
    {
        var ctx = _http.HttpContext;
        try
        {
            _db.AuditLog.Add(new AuditEntry
            {
                At = DateTime.UtcNow,
                Username = Truncate(username ?? ctx?.User.Identity?.Name ?? "-", 50),
                Action = Truncate(action, 40),
                Detail = Truncate(detail, 500),
                Ip = ctx?.Connection.RemoteIpAddress?.ToString()
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo registrar la auditoría {Action}: {Detail}", action, detail);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
