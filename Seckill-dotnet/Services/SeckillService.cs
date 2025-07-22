using Microsoft.EntityFrameworkCore;
using RedLockNet;
using Seckill_dotnet.Infrastructure;
using Seckill_dotnet.Models;
using Seckill_dotnet.RabbitMQ;
using Seckill_dotnet.Redis;

namespace Seckill_dotnet.Services
{
    /// <summary>
    /// 秒杀服务
    /// </summary>
    public class SeckillService
    {
        private readonly RedisService _redisService;
        private readonly SeckillContext _context;
        private readonly RabbitMQService _rabbitMQService;
        private readonly IDistributedLockFactory _lockFactory;
        private readonly ILogger<SeckillService> _logger;

        public SeckillService(RedisService redisService, SeckillContext context, RabbitMQService rabbitMQService, IDistributedLockFactory lockFactory, ILogger<SeckillService> logger)
        {
            _redisService = redisService;
            _context = context;
            _rabbitMQService = rabbitMQService;
            _lockFactory = lockFactory;
            _logger = logger;
        }

        /// <summary>
        /// 处理秒杀请求：减少库存、记录订单
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="productId"></param>
        /// <returns>秒杀结果</returns>
        public async Task<SeckillResult> ProcessSeckillAsync(string userId, string productId)
        {
            if (_redisService.IsRedisAvailable)
            {
                var isSecKill = await _redisService.CanUserSeckillAsync(userId, productId);

                // 1. 检查用户是否已秒杀过
                if (!isSecKill)
                {
                    return SeckillResult.Failure("用户已秒杀过该商品");
                }

                // 2. 从 Redis 获取库存，并尝试减少库存
                var stock = await _redisService.DecrementInventoryAsync(productId);

                if (stock <= 0)
                {
                    return SeckillResult.Failure("库存不足");
                }

                // 3. 秒杀成功，记录用户秒杀状态到 Redis
                bool setResult = await _redisService.SetUserSeckillResultAsync(userId, productId);

                if (!setResult)
                {
                    return SeckillResult.Failure("用户抢单失败"); // 设置用户秒杀状态失败
                }
                // 4. 发送订单消息到RabbitMQ
                OrderMessage orderMessage = new OrderMessage
                {
                    UserId = userId,
                    ProductId = productId,
                    OrderId = Guid.NewGuid().ToString(),
                };

                await _rabbitMQService.SendAsync("", "seckill_orders", orderMessage);


                return SeckillResult.Success();  // 秒杀成功
            }
            else
            {
                // redis服务不可用时，使用数据库回退处理
                SeckillRequest request = new SeckillRequest()
                {
                    UserId = userId,
                    ProductId = productId
                };
                return await DatabaseFallbackSeckillAsync(request);
            }
        }


        /// <summary>
        /// 当Redis服务不可用时，使用数据库回退处理秒杀请求
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private async Task<SeckillResult> DatabaseFallbackSeckillAsync(SeckillRequest request)
        {
            // 使用数据库分布式锁
            string resourceId = string.Format(SeckillConst.SeckillLockResourceId, request.ProductId); 
            using var redLock = await _lockFactory.CreateLockAsync(
                resourceId,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(0.5));

            if (!redLock.IsAcquired)
                return SeckillResult.Failure("系统繁忙，请重试");

            // 数据库事务处理
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var product = await _context.Products
                    .Where(p => p.Id == request.ProductId)
                    .FirstOrDefaultAsync();

                if (product == null)
                    return SeckillResult.Failure("商品不存在");

                if (product.Stock <= 0)
                    return SeckillResult.Failure("库存不足");

                // 扣减库存
                product.Stock--;
                // product.Version++; // 乐观锁  ValueGeneratedOnAddOrUpdate 已自动新增版本号
                product.LastSyncTime = DateTime.Now;

                // 创建订单
                var order = new Infrastructure.Order
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    ProductId = request.ProductId,
                    OrderTime = DateTime.Now
                };

                await _context.Orders.AddAsync(order);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return SeckillResult.Success(order.Id);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "库存并发冲突");
                return SeckillResult.Failure("库存冲突，请重试");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "数据库秒杀异常");
                return SeckillResult.Failure("系统错误");
            }
        }
    }
}
