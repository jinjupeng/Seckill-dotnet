using StackExchange.Redis;

namespace Seckill_dotnet.Services
{
    public class InventoryService
    {
        private readonly IConnectionMultiplexer _redis;

        public InventoryService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        /**
         * 初始化商品库存
         * @param productId 商品ID
         * @param stock 初始库存数量
         */
        public async Task InitializeProductStockAsync(string productId, int stock)
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync($"product_stock:{productId}", stock);
        }
    }
}
