using System;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Tops;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Background
{
    public class TopWorker : BackgroundService
    {
        readonly ILogger<TopWorker> _logger;

        public TopWorker(ILogger<TopWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (AppInit.conf?.top == null || !AppInit.conf.top.enable)
                return;

            _logger.LogInformation("top worker started");
            try
            {
                await Run(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "top worker terminated unexpectedly");
            }
        }

        static async Task Run(CancellationToken cancellationToken)
        {
            await Task.Delay(30_000, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try { await TopDb.BuildAsync(cancellationToken); }
                catch (Exception ex)
                {
                    JacRedLog.Error(JacRedLogCategories.Top, $"error / {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(Math.Max(5, AppInit.conf?.top?.refreshMinutes ?? 15)), cancellationToken);
            }
        }
    }
}
