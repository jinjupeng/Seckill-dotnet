using Microsoft.EntityFrameworkCore;
using RedLockNet;
using Seckill_dotnet.Infrastructure;
using Seckill_dotnet.Models;

namespace Seckill_dotnet.Services
{
    public class OrderService
    {
        private readonly SeckillContext _context; 
        private readonly IDistributedLockFactory _lockFactory;
        private readonly ILogger<OrderService> _logger;
        public OrderService(SeckillContext context, IDistributedLockFactory lockFactory, ILogger<OrderService> logger)
        {
            _context = context;
            _lockFactory = lockFactory;
            _logger = logger;
        }

        public async Task<bool> CreateOrderAsync(OrderMessage message)
        {
            // 使用分布式锁防止重复消费（例如，在分布式部署中，多个消费者实例可能同时处理同一个消息，但通过分布式锁可以保证只有一个实例处理）
            string resourceId = string.Format(SeckillConst.SeckillLockResourceId, message.ProductId);
            using (var redLock = await _lockFactory.CreateLockAsync(resourceId, TimeSpan.FromSeconds(30)))
            {
                if (redLock.IsAcquired)
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        // 检查订单是否已存在
                        var orderExisting = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == message.OrderId);

                        if (orderExisting != null)
                        {
                            Console.WriteLine("订单已存在：" + message.OrderId + " UserId：" + message.UserId);
                            return true; // 订单已存在，直接返回
                        }


                        // 创建新的订单，保存到数据库
                        var order = new Infrastructure.Order
                        {
                            Id = message.OrderId,
                            UserId = message.UserId,
                            ProductId = message.ProductId,
                            OrderTime = DateTime.Now
                        };

                        await _context.Orders.AddAsync(order);


                        // 更新数据库库存（可选，Redis为主库存）
                        var productExisting = await _context.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == message.ProductId);

                        if (productExisting == null || productExisting.Stock <= 0)
                        {
                            return true; // 商品不存在或者库存为0
                        }
                        productExisting.Stock--; // 扣减库存
                        productExisting.LastSyncTime = DateTime.Now;
                        _context.Update(productExisting);

                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        return true; // 订单创建成功
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogWarning(ex, "库存并发冲突");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "数据库秒杀异常");
                        return false;
                    }
                }
                else
                {
                    return false; // 获取锁失败，表示其他实例正在处理该订单
                }
            }
        }

        /// <summary>
        /// 初始化商品库存，将商品库存写入到数据库
        /// 正常情况下是将数据库的库存数据同步到Redis缓存，但是为了模拟数据，将库存数据通过接口同步给缓存和数据库
        /// </summary>
        /// <param name="productId"></param>
        /// <param name="stock"></param>
        /// <returns></returns>
        public async Task InitializeProductStockAsync(string productId, int stock)
        {
            var product = _context.Products.AsNoTracking().FirstOrDefault(x => x.Id == productId);
            if (product == null)
            {
                product = new Product();
                product.Id = productId;
                product.Name = productId;
                product.Stock = stock;
                product.LastSyncTime = DateTime.Now;

                await _context.AddAsync(product);
            }
            else
            {
                product.Stock = stock;
                product.LastSyncTime = DateTime.Now;

                _context.Update(product);
            }

            await _context.SaveChangesAsync();
        }
    }
}
