using Microsoft.EntityFrameworkCore;
using Seckill_dotnet.Infrastructure;
using StackExchange.Redis;

namespace Seckill_dotnet.Services
{
    /**
    *库存服务
    */
    public class InventoryService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly SeckillContext _seckillContext;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(IConnectionMultiplexer redis, SeckillContext seckillContext, ILogger<InventoryService> logger)
        {
            _redis = redis;
            _seckillContext = seckillContext;
            _logger = logger;
        }

        /**
         * 初始化商品库存
         * @param productId 商品ID
         * @param stock 初始库存数量
         */
        public async Task InitializeProductStockAsync(string productId, int stock)
        {
            // 正常情况下是将数据库的库存数据同步到Redis缓存，但是为了模拟数据，将库存数据通过接口同步给缓存和数据库
            
            var db = _redis.GetDatabase();
            await db.StringSetAsync($"product_stock:{productId}", stock);

            var product = _seckillContext.Products.AsNoTracking().FirstOrDefault(x => x.Id == productId);
            if (product == null)
            {
                product = new Product();
                product.Id = productId;
                product.Name = productId;
                product.Stock = stock;

                await _seckillContext.AddAsync(product);
            }
            else
            {
                product.Stock = stock;

                _seckillContext.Update(product);
            }

            await _seckillContext.SaveChangesAsync();
        }
    }
}
