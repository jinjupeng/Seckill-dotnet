using RedLockNet;
using Seckill_dotnet.Infrastructure;
using Seckill_dotnet.Models;
using Seckill_dotnet.RabbitMQ;
using StackExchange.Redis;

namespace Seckill_dotnet.Services
{
    public class SeckillService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly SeckillContext _context;
        private readonly RabbitMQService _rabbitMQService;
        private readonly IDistributedLockFactory _lockFactory;

        public SeckillService(IConnectionMultiplexer redis, SeckillContext context, RabbitMQService rabbitMQService, IDistributedLockFactory lockFactory)
        {
            _redis = redis;
            _context = context;
            _rabbitMQService = rabbitMQService;
            _lockFactory = lockFactory;
        }

        /**
         * 处理秒杀请求：减少库存、记录订单
         * @param userId 用户ID
         * @param productId 商品ID
         * @return 是否秒杀成功
         */
        public async Task<bool> ProcessSeckillAsync(string userId, string productId)
        {
            var db = _redis.GetDatabase();

            // 1. 检查用户是否已秒杀过
            if (!await CanUserSeckillAsync(userId, productId))
            {
                return false;  // 用户已秒杀过
            }

            // 2. 从 Redis 获取库存，并尝试减少库存
            var stockKey = $"product_stock:{productId}";
            var stock = await db.StringDecrementAsync(stockKey);

            if (stock < 0)
            {
                return false;  // 库存不足，秒杀失败
            }

            // 3. 秒杀成功，记录用户秒杀状态到 Redis
            bool setResult = await db.StringSetAsync($"user:{userId}:seckill:{productId}", "true", TimeSpan.FromMinutes(10));

            if (!setResult)
            {
                return false;  // 设置用户秒杀状态失败
            }
            // 4. 发送订单消息到RabbitMQ
            OrderMessage orderMessage = new OrderMessage
            {
                UserId = userId,
                ProductId = productId,
                OrderId = Guid.NewGuid().ToString(),
            };

            await _rabbitMQService.SendAsync("", "seckill_orders", orderMessage);


            return true;  // 秒杀成功
        }

        /**
         * 检查用户是否已经秒杀过该商品
         * @param userId 用户ID
         * @param productId 商品ID
         * @return 是否可以秒杀
         */
        private async Task<bool> CanUserSeckillAsync(string userId, string productId)
        {
            var db = _redis.GetDatabase();
            var userKey = $"user:{userId}:seckill:{productId}";
            var result = await db.StringGetAsync(userKey);
            return result.IsNullOrEmpty;  // 如果返回 null，表示用户没有秒杀过
        }
    }
}
