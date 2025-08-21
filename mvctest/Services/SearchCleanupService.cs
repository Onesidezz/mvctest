using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace mvctest.Services
{
    public class SearchCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SearchCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15); // Run every 15 minutes

        public SearchCleanupService(IServiceScopeFactory scopeFactory, ILogger<SearchCleanupService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🧹 Search Cleanup Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_cleanupInterval, stoppingToken);
                    
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await PerformCleanup();
                    }
                }
                catch (TaskCanceledException)
                {
                    // Service is being stopped
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in Search Cleanup Service");
                    
                    // Continue running even if cleanup fails
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("🛑 Search Cleanup Service stopped");
        }

        private async Task PerformCleanup()
        {
            try
            {
                _logger.LogDebug("🧹 Starting search cleanup cycle");

                using var scope = _scopeFactory.CreateScope();
                
                // In a real application, you would:
                // 1. Get all active sessions from your session store
                // 2. Clean up expired search data
                // 3. Update statistics/metrics
                
                var cleanedCount = await CleanupExpiredSearches();
                
                if (cleanedCount > 0)
                {
                    _logger.LogInformation("✅ Cleaned up {CleanedCount} expired searches", cleanedCount);
                }
                else
                {
                    _logger.LogDebug("🔍 No expired searches found to clean up");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to perform search cleanup");
            }
        }

        private async Task<int> CleanupExpiredSearches()
        {
            // This is a simplified version. In production, you would:
            // - Use distributed cache with expiration
            // - Query your session store directly
            // - Use database to track search sessions
            
            await Task.Delay(100); // Simulate cleanup work
            
            var cleanedCount = 0;
            
            try
            {
                // Placeholder for actual cleanup logic
                // In a real implementation, you'd iterate through session stores
                // and remove expired search data based on timestamps
                
                _logger.LogDebug("🔍 Search cleanup completed. Items cleaned: {CleanedCount}", cleanedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during search cleanup");
            }

            return cleanedCount;
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🛑 Stopping Search Cleanup Service...");
            await base.StopAsync(stoppingToken);
        }
    }
}