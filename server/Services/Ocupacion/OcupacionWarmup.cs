namespace UsuariosRetirados.Server.Services.Ocupacion;

/// <summary>
/// Al arrancar crea el esquema si falta y precalcula el dashboard sin filtros y los
/// combos de filtros (sobre 1,85 M de registros tardan ~20 s), para que la primera
/// visita no espere.
/// </summary>
public class OcupacionWarmup : BackgroundService
{
    private readonly OcupacionStore _store;
    private readonly OcupacionService _service;
    private readonly ILogger<OcupacionWarmup> _logger;

    public OcupacionWarmup(OcupacionStore store, OcupacionService service, ILogger<OcupacionWarmup> logger)
    {
        _store = store;
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Run(_store.EnsureSchema, stoppingToken);
            _logger.LogInformation("Ocupación Edificios: base en {Path}", _store.DbPath);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _service.GetFiltrosAsync(stoppingToken);
            await _service.GetStatsAsync(new OcupacionFiltro(null, null, null, null, null, null, null, null), stoppingToken);
            _logger.LogInformation("Ocupación Edificios: dashboard precalculado en {Ms} ms", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocupación Edificios: no se pudo preparar la base {Path}", _store.DbPath);
        }
    }
}
