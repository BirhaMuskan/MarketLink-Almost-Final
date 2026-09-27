namespace MarketLink.Services
{
    public sealed class AnomalyDetectionWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AnomalyDetectionWorker> _logger;

        public AnomalyDetectionWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<AnomalyDetectionWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            // Let the application finish starting first.
            await Task.Delay(
                TimeSpan.FromSeconds(20),
                stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope =
                        _scopeFactory.CreateScope();

                    var service =
                        scope.ServiceProvider
                            .GetRequiredService<IAnomalyDetectionService>();

                    await service.ScanAllAsync(
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Anomaly detection background scan failed.");
                }

                await Task.Delay(
                    TimeSpan.FromMinutes(15),
                    stoppingToken);
            }
        }
    }
}
