using Microsoft.EntityFrameworkCore;
using PWExtendedApp.Server.Data;

namespace PWExtendedApp.Server.Services;

/// <summary>
/// Mantiene en memoria el padrón ya leído (<see cref="PadronSnapshot"/>). Se vuelve a leer
/// solo cuando cambia el archivo (ruta, tamaño o fecha de modificación), así cada vista previa
/// filtra en memoria en milisegundos en vez de releer los ~165 MB.
/// </summary>
public class PadronCache
{
    private readonly ICsvStreamingEngine _engine;
    private readonly ILogger<PadronCache> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile PadronSnapshot? _current;

    public PadronCache(ICsvStreamingEngine engine, ILogger<PadronCache> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    private static bool IsCurrent(PadronSnapshot? snap, string path, FileInfo fi) =>
        snap != null &&
        string.Equals(snap.Path, path, StringComparison.OrdinalIgnoreCase) &&
        snap.Length == fi.Length &&
        snap.LastWriteUtc == fi.LastWriteTimeUtc;

    /// <summary>Devuelve el padrón vigente; lo lee (una sola vez, aunque haya varias llamadas) si cambió.</summary>
    public PadronSnapshot Get(string path)
    {
        var fi = new FileInfo(path);
        var snap = _current;
        if (IsCurrent(snap, path, fi)) return snap!;

        _loadLock.Wait();
        try
        {
            fi.Refresh();
            snap = _current;
            if (IsCurrent(snap, path, fi)) return snap!;

            snap = _engine.Load(path);
            _current = snap;
            _logger.LogInformation("Padrón cargado en memoria: {Rows} filas de {Path} en {Ms} ms",
                snap.Rows.Count, path, snap.LoadMs);
            return snap;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Ruta del padrón: la de Administración (AppConfigs) o la de appsettings.</summary>
    public static async Task<string> ResolveInputPathAsync(AppDbContext db, IConfiguration config)
    {
        var cfg = await db.AppConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Key == "InputPath");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Value)) return cfg.Value;
        return config["AppPaths:InputPath"] ?? string.Empty;
    }
}

/// <summary>
/// Precarga el padrón al arrancar y revisa cada 5 minutos si el archivo cambió (p. ej. lo
/// actualizó el script diario), para que ninguna vista previa tenga que esperar la lectura.
/// </summary>
public class PadronWarmup : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly PadronCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<PadronWarmup> _logger;

    public PadronWarmup(IServiceScopeFactory scopes, PadronCache cache, IConfiguration config, ILogger<PadronWarmup> logger)
    {
        _scopes = scopes;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Deja arrancar primero el resto de la app (seed de la base, etc.).
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ContinueWith(_ => { });
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string path;
                using (var scope = _scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    path = await PadronCache.ResolveInputPathAsync(db, _config);
                }
                if (File.Exists(path))
                {
                    await Task.Run(() => _cache.Get(path), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo precargar el padrón");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
