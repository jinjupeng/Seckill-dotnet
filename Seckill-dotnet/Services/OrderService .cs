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
        public OrderService(SeckillContext context, IDistributedLockFactory lockFactory)
        {
            _context = context;
            _lockFactory = lockFactory;
        }

        public async Task CreateOrderAsync(OrderMessage message)
        {
            // 使用分布式锁防止重复消费（例如，在分布式部署中，多个消费者实例可能同时处理同一个消息，但通过分布式锁可以保证只有一个实例处理）
            string resourceId = "order_lock:" + message.OrderId;
            using (var redLock = await _lockFactory.CreateLockAsync(resourceId, TimeSpan.FromSeconds(30)))
            {
                if (redLock.IsAcquired)
                {
                    // 检查订单是否已存在
                    var orderExisting = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == message.OrderId);

                    if (orderExisting != null)
                    {
                        return; // 订单已存在，直接返回
                    }


                    // 将订单保存到数据库
                    var order = new Order
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = message.UserId,
                        ProductId = message.ProductId,
                        OrderTime = DateTime.UtcNow
                    };

                    await _context.Orders.AddAsync(order);


                    //// 更新数据库库存（可选，Redis为主库存）
                    //var productExisting = await _context.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == message.ProductId);

                    //if (productExisting == null || productExisting.Stock <= 0)
                    //{
                    //    return; // 商品不存在或者库存为0
                    //}
                    //productExisting.Stock = productExisting.Stock - 1;
                    //_context.Update(productExisting);

                    await _context.SaveChangesAsync();
                }
            }
        }
    }
}
