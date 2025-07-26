using BotTradingCrypto.Application;

namespace BotTradingCrypto.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;

        public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                // Resolve the worker from the scope
                var tradingService = scope.ServiceProvider.GetRequiredService<ISpotGridTradingService>();
                //await tradingService.ConnectSocketTradingAsync("BTCUSDT");
                _ = Task.Run(async () =>
                {
                    //await tradingService.ConnectSocketTradingAsync("BTCUSDT");
                });

            }
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }

        }
    }
}
