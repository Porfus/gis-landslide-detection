using it.gis_landslide_detection.web.Data;
using Microsoft.EntityFrameworkCore;

namespace it.gis_landslide_detection.web.Services
{
    /// <summary>
    /// Segnala che routing_edges va rigenerato. Il lavoro vero e proprio gira in
    /// background (vedi RoutingTopologyRefreshService): l'endpoint HTTP che ha
    /// creato/modificato/cancellato un sentiero torna subito, senza aspettare i
    /// 20+ secondi di refresh_routing_topology().
    /// </summary>
    public interface IRoutingTopologyRefreshQueue
    {
        void RequestRefresh();
    }

    /// <summary>
    /// Esegue refresh_routing_topology() in background, con debounce: se arrivano
    /// più richieste ravvicinate (es. l'utente cancella/crea più sentieri di fila),
    /// vengono coalizzate in un solo refresh invece di rieseguirlo ogni volta.
    /// Usa un proprio scope/DbContext perché gira oltre la vita della richiesta
    /// HTTP che ha innescato la richiesta di refresh.
    /// </summary>
    public class RoutingTopologyRefreshService : BackgroundService, IRoutingTopologyRefreshQueue
    {
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(3);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RoutingTopologyRefreshService> _logger;
        private readonly SemaphoreSlim _signal = new(0, 1);

        public RoutingTopologyRefreshService(IServiceScopeFactory scopeFactory, ILogger<RoutingTopologyRefreshService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void RequestRefresh()
        {
            // Se c'è già un refresh in coda, non serve accodarne un altro:
            // quando partirà rifletterà comunque anche questa modifica.
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(stoppingToken);

                    // Debounce: aspetta che le modifiche ravvicinate smettano di arrivare
                    // prima di rigenerare la topologia una sola volta.
                    await Task.Delay(DebounceDelay, stoppingToken);
                    if (_signal.CurrentCount > 0)
                    {
                        await _signal.WaitAsync(stoppingToken);
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await context.Database.ExecuteSqlRawAsync("SELECT refresh_routing_topology();", stoppingToken);
                    _logger.LogInformation("[routing] Topologia di routing aggiornata.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[routing] Errore durante l'aggiornamento della topologia di routing.");
                }
            }
        }
    }
}
