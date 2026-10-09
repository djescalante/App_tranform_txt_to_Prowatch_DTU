using Microsoft.Data.Sqlite;
using PWExtendedApp.Server.Services.Ocupacion;

namespace PWExtendedApp.Server.Services;

/// <summary>Ruta absoluta de la base de usuarios/historial/VIP (resuelta en Program.cs).</summary>
public record UsersDbPath(string Path);

public record BackupFile(string Name, long SizeBytes, DateTime CreatedAt);

public record BackupRun(DateTime At, bool Success, string Message, long ElapsedMs);

public record BackupStatus(string Directory, int Hour, int Keep, BackupRun? LastRun, List<BackupFile> Files);

/// <summary>
/// Copias de seguridad de las bases SQLite (usuarios/historial/VIP y prowatch.db) con la API
/// de backup en caliente de SQLite: la copia es consistente aunque la app esté en uso.
/// Configuración: Backup:Dir (por defecto &lt;carpeta de la base&gt;\backups), Backup:Hour (2) y
/// Backup:Keep (7 copias por base).
/// </summary>
public class BackupService
{
    private readonly UsersDbPath _usersDb;
    private readonly OcupacionStore _ocupacion;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string Directory { get; }
    public int Hour { get; }
    public int Keep { get; }
    public BackupRun? LastRun { get; private set; }

    public BackupService(UsersDbPath usersDb, OcupacionStore ocupacion, IConfiguration config, ILogger<BackupService> logger)
    {
        _usersDb = usersDb;
        _ocupacion = ocupacion;
        _logger = logger;

        var dir = config["Backup:Dir"];
        Directory = string.IsNullOrWhiteSpace(dir)
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(usersDb.Path)!, "backups")
            : System.IO.Path.GetFullPath(dir);
        Hour = Math.Clamp(config.GetValue("Backup:Hour", 2), 0, 23);
        Keep = Math.Max(1, config.GetValue("Backup:Keep", 7));
    }

    private IEnumerable<(string Prefix, string Path)> Databases()
    {
        yield return ("usuarios_retirados", _usersDb.Path);
        yield return ("prowatch", _ocupacion.DbPath);
    }

    public async Task<BackupRun> RunAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var done = new List<string>();

            foreach (var (prefix, path) in Databases())
            {
                if (!File.Exists(path)) continue;
                var dest = System.IO.Path.Combine(Directory, $"{prefix}_{stamp}.db");
                await Task.Run(() =>
                {
                    using var src = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
                    using var dst = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource = dest, Pooling = false }.ToString());
                    src.Open();
                    src.BackupDatabase(dst);
                }, ct);
                Prune(prefix);
                done.Add(System.IO.Path.GetFileName(dest));
            }

            LastRun = new BackupRun(DateTime.Now, true,
                done.Count > 0 ? $"Copias creadas: {string.Join(", ", done)}" : "No había bases para respaldar.",
                sw.ElapsedMilliseconds);
            _logger.LogInformation("Respaldo completado en {Ms} ms: {Files}", sw.ElapsedMilliseconds, string.Join(", ", done));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastRun = new BackupRun(DateTime.Now, false, $"Error: {ex.Message}", sw.ElapsedMilliseconds);
            _logger.LogError(ex, "Falló el respaldo de las bases en {Dir}", Directory);
        }
        finally
        {
            _lock.Release();
        }
        return LastRun!;
    }

    /// <summary>Conserva las <see cref="Keep"/> copias más recientes de cada base.</summary>
    private void Prune(string prefix)
    {
        var old = new DirectoryInfo(Directory).GetFiles($"{prefix}_*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(Keep);
        foreach (var f in old)
        {
            try { f.Delete(); }
            catch (IOException ex) { _logger.LogWarning(ex, "No se pudo borrar la copia antigua {File}", f.Name); }
        }
    }

    public BackupStatus GetStatus()
    {
        var files = System.IO.Directory.Exists(Directory)
            ? new DirectoryInfo(Directory).GetFiles("*.db")
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTime))
                .ToList()
            : [];
        return new BackupStatus(Directory, Hour, Keep, LastRun, files);
    }
}

/// <summary>Ejecuta el respaldo todos los días a la hora configurada (Backup:Hour).</summary>
public class BackupScheduler : BackgroundService
{
    private readonly BackupService _backup;

    public BackupScheduler(BackupService backup) => _backup = backup;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var next = now.Date.AddHours(_backup.Hour);
            if (next <= now) next = next.AddDays(1);
            try
            {
                await Task.Delay(next - now, stoppingToken);
                await _backup.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
