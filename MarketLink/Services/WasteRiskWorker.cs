namespace MarketLink.Services
{
    public sealed class WasteRiskWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WasteRiskWorker> _logger;

        public WasteRiskWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<WasteRiskWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(30),
                stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope =
                        _scopeFactory.CreateScope();

                    var service =
                        scope.ServiceProvider
                            .GetRequiredService<IWasteRiskService>();

                    await service.ScanAllAsync(
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Waste-risk background scan failed.");
                }

                await Task.Delay(
                    TimeSpan.FromMinutes(30),
                    stoppingToken);
            }
        }
    }
}
