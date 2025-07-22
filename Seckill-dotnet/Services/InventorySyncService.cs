using Microsoft.EntityFrameworkCore;
using Seckill_dotnet.Infrastructure;
using Seckill_dotnet.Models;
using Seckill_dotnet.Redis;
using StackExchange.Redis;

namespace Seckill_dotnet.Services
{
    /// <summary>
    /// Redis库存同步服务（后台服务）
    /// </summary>
    public class InventorySyncService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<InventorySyncService> _logger;
        private Timer? _timer;

        public InventorySyncService(IServiceProvider services, ILogger<InventorySyncService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _timer = new Timer(state =>
            {
                _ = SyncInventory(state);
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
            return Task.CompletedTask;
        }

        private async Task SyncInventory(object state)
        {
            using var scope = _services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SeckillContext>();
            var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
            var fallbackService = scope.ServiceProvider.GetRequiredService<RedisService>();

            if (!await fallbackService.IsRedisAvailableAsync()) return;

            try
            {
                var products = await dbContext.Products
                    .AsNoTracking()
                    .Where(p => p.LastSyncTime < DateTime.Now.AddMinutes(-5))
                    .ToListAsync();

                var redisDb = redis.GetDatabase();
                var batch = redisDb.CreateBatch();

                foreach (var product in products)
                {
                    await batch.StringSetAsync(string.Format(SeckillConst.SeckillProductStockKey, product.Id), product.Stock);
                }

                batch.Execute();

                // 更新同步时间
                await dbContext.Products
                    .Where(p => products.Select(x => x.Id).Contains(p.Id))
                    .ExecuteUpdateAsync(p =>
                        p.SetProperty(x => x.LastSyncTime, DateTime.Now));
            }
            catch (RedisException ex)
            {
                _logger.LogError(ex, "库存同步失败");
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return base.StopAsync(cancellationToken);
        }
    }
}
