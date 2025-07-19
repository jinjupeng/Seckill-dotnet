# 基于.NET8的秒杀系统设计
使用Redis作为缓存和分布式锁，数据库MySql持久化数据，消息队列RabbitMQ进行异步处理和解耦。
### 系统架构设计
1. **前端**：接收用户请求，进行初步的限流（如Nginx限流）和验证（如验证码）。
2. **网关层**：进行身份验证、限流、请求分发等。
3. **应用层**：处理秒杀业务逻辑，包括Redis预减库存、消息队列发送等。
4. **服务层**：包括Redis服务（缓存和分布式锁）、数据库服务、消息队列服务。
5. **数据库层**：存储商品、订单等数据。

### 关键技术点
1. **Redis**：
   - 缓存商品库存（预热）。
   - 使用分布式锁（如RedLock）防止超卖。
   - 原子操作（如DECR）进行库存预减。
2. **消息队列**：
   - 异步处理订单创建，提高响应速度。
   - 削峰填谷，避免数据库压力过大。
3. **数据库**：
   - 最终库存扣减和订单创建。
   - 使用事务保证数据一致性。
4. **限流**：
   - 网关层限流（如令牌桶）。
   - 应用层限流（如SemaphoreSlim）。

### 代码实现
#### 1. 商品库存预热到Redis
在秒杀开始前，将商品库存加载到Redis中。
```csharp
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
```

#### 2. 秒杀接口
处理秒杀请求，进行库存预减，然后发送消息到消息队列。
```csharp
        [HttpPost("Order")]
        public async Task<IActionResult> Order([FromBody] SeckillRequest request)
        {
            // 1. 验证用户身份（JWT等）


            var result = await _seckillService.ProcessSeckillAsync(request.UserId, request.ProductId);
            if (result)
            {
                return Ok("秒杀成功！");
            }

            return BadRequest("秒杀失败，库存不足或已经秒杀过");
        }
```

#### 3. Redis库存操作
使用Redis的原子操作来保证库存扣减的线程安全。
```csharp
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

```

#### 4. 消息队列服务
使用RabbitMQ发送消息。
```csharp
    public class RabbitMQService
    {
        private readonly IRabbitMQConnection _connection;
        private readonly ILogger<RabbitMQService> _logger;

        public RabbitMQService(IRabbitMQConnection connection, ILogger<RabbitMQService> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger;
        }


        public async Task SendAsync(string exchange, string routingKey, object message, bool mandatory = false, CancellationToken cancellationToken = default)
        {

            try
            {
                using var channel = await _connection.CreateChannel();
                var mesjson = JsonSerializer.Serialize(message);
                Console.WriteLine("发送消息：" + mesjson);
                var body = Encoding.UTF8.GetBytes(mesjson);
                var properties = new BasicProperties
                {
                    Persistent = true // 设置消息持久化
                };
                await channel.BasicPublishAsync(exchange, routingKey, false, properties, body, cancellationToken);

            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine($"Operation was canceled: {ex.Message}");
                //throw; // Re-throw if you want to propagate the cancellation
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                //throw; // Re-throw if you want to propagate the error
            }
        }

        public async Task ReceiveAsync(string queueName, Func<IChannel, byte[], Task> callback, CancellationToken cancellationToken = default)
        {
            var channel = await _connection.CreateChannel();
            // 声明队列
            await channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                try
                {
                    // 直接传递 model 和 body 给 callback，不需要转换
                    await callback(channel, body);
                }
                catch(Exception ex)
                {
                    _logger.LogError("消息队列接收消息出现异常：{}，{}", ex.Message, ex.StackTrace);
                }
                finally
                {
                    //await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                }
            };
            await channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
            // Prevent the method from returning immediately
            await Task.Delay(-1, cancellationToken);
        }
    }

```

#### 5. 消费者处理订单
从消息队列中取出订单消息，进行数据库操作（创建订单、扣减库存）。
```csharp
    /** 
     * RabbitMQ消费者（订单持久化）
     */
    public class OrderProcessingWorker : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly RabbitMQService _rabbitMQService;
        private readonly ILogger<OrderProcessingWorker> _logger;

        public OrderProcessingWorker(IServiceProvider services, ILogger<OrderProcessingWorker> logger, RabbitMQService rabbitMQService)
        {
            _services = services;
            _logger = logger;
            _rabbitMQService = rabbitMQService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 服务启动日志
            _logger.LogInformation("OrderProcessingWorker started at: {}", DateTime.Now);

            // 使用循环保持服务持续运行
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 启动消息接收
                    var receiveTask = _rabbitMQService.ReceiveAsync("seckill_orders", async (channel, body) =>
                    {
                        using var scope = _services.CreateScope();
                        var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();

                        string mesjson = Encoding.UTF8.GetString(body);
                        Console.WriteLine("收到消息：" + mesjson);
                        var message = JsonSerializer.Deserialize<OrderMessage>(mesjson);

                        await orderService.CreateOrderAsync(message);

                        await channel.BasicAckAsync(deliveryTag: default, false, stoppingToken);
                    }, stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // 添加延迟，避免空循环消耗CPU资源
                }
                catch (Exception ex)
                {
                    // 异常处理（记录日志等）
                    _logger.LogError(ex, "Error processing orders");

                    // 异常后延迟重试
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }

            }
            // 服务停止日志
            _logger.LogInformation("OrderProcessingWorker stopped at: {}", DateTime.Now);
        }
    }

```

#### 6. 订单服务
在数据库中创建订单，并扣减库存（使用数据库事务保证一致性）。
```csharp
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
            // 使用分布式锁防止重复消费
            using (var redLock = await _lockFactory.CreateLockAsync("order_lock", TimeSpan.FromSeconds(30)))
            {
                if (redLock.IsAcquired)
                {
                    // 检查订单是否已存在
                    var existing = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == message.OrderId);

                    if (existing != null)
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
                    await _context.SaveChangesAsync();


                    // 更新数据库库存（可选，Redis为主库存）


                }
            }
        }
    }


```

### 使用JMeter模拟高并发请求测试
验证订单在Redis中和数据库中数据最终一致性
截图在/test文件夹下
线程设置为300，持续时间100秒，总库存数是200，经验证，一共有200不同的用户下单成功，其他人秒杀失败，Redis数据和数据库数据一致



