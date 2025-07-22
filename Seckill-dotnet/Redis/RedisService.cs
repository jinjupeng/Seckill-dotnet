using Polly;
using Polly.CircuitBreaker;
using Seckill_dotnet.Models;
using StackExchange.Redis;

namespace Seckill_dotnet.Redis
{
    /// <summary>
    /// Redis服务封装，加入熔断和降级处理
    /// </summary>
    public class RedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisService> _logger;
        private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
        // 使用volatile确保状态可见性（.NET 5+）
        private volatile bool _isRedisAvailable = true;
        public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
        {
            _redis = redis;
            _logger = logger;
            // 定义熔断策略：当连续3次失败，熔断10秒
            _circuitBreakerPolicy = Policy
                .Handle<RedisException>()
                .Or<RedisConnectionException>()
                .Or<RedisTimeoutException>()
                .Or<RedisCommandException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3, // 当连续请求3次失败，熔断器将打开
                    durationOfBreak: TimeSpan.FromSeconds(10),
                    onBreak: (ex, breakDelay) =>
                    {
                        _logger.LogInformation($"Redis熔断器开启，熔断时间：{breakDelay.TotalSeconds}秒。");
                        // 表示Redis不可用
                        SetRedisStatus(false);
                        //熔断器回调（onBreak和onReset）是由Polly的策略执行的，而Polly的策略执行是线程安全的，所以我们在回调中修改字段是安全的。同时，我们通过volatile确保其他线程能立即看到变化。
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Redis熔断器重置。");
                        // 表示Redis恢复可用
                        SetRedisStatus(true);
                    },
                    onHalfOpen: async () =>
                    {
                        _logger.LogInformation("Redis熔断器半开启，尝试恢复。");
                        // 半开启状态，尝试恢复连接
                        await RetryAsync();
                    }
                );
        }

        /// <summary>
        /// 读取时直接访问（无Interlocked开销）
        /// </summary>
        public bool IsRedisAvailable => _isRedisAvailable == true;

        /// <summary>
        /// 预热库存，将商品库存预设到Redis中
        /// </summary>
        /// <param name="productId"></param>
        /// <param name="stock"></param>
        /// <returns></returns>
        public async Task<bool> PreheatInventoryAsync(string productId, int stock)
        {
            // 使用熔断策略包裹
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                string stockKey = string.Format(SeckillConst.SeckillProductStockKey, productId);
                return await db.StringSetAsync(stockKey, stock);
            });
        }

        /// <summary>
        /// 扣减库存
        /// </summary>
        /// <param name="productId"></param>
        /// <returns></returns>
        public async Task<long> DecrementInventoryAsync(string productId)
        {
            // 使用熔断策略包裹
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                // 使用Lua脚本原子性地检查库存并减少，当库存大于0时减少，否则返回-1；和StringDecrement方法的区别和联系，都是原子性操作，但Lua脚本可以返回0，而StringDecrement会一直减小到负数。
                var script = LuaScript.Prepare(
                    "local stock = tonumber(redis.call('get', KEYS[1])) " +
                    "if stock and stock > 0 then " +
                    "   return redis.call('decr', KEYS[1]) " +
                    "else " +
                    "   return -1 " +
                    "end"
                );
                string stockKey = string.Format(SeckillConst.SeckillProductStockKey, productId);
                var result = await db.ScriptEvaluateAsync(script, new RedisKey[] { stockKey });
                return (long)result;
            });
        }

        ///// <summary>
        ///// 扣减库存
        ///// </summary>
        ///// <param name="productId"></param>
        ///// <returns></returns>
        //public async Task<long> DecrementInventoryAsync(string productId)
        //{
        //    // 使用熔断策略包裹
        //    return await _circuitBreakerPolicy.ExecuteAsync(async () =>
        //    {
        //        var db = _redis.GetDatabase();
        //        string stockKey = string.Format(SeckillConst.SeckillProductStockKey, productId);
        //        var stock = await db.StringDecrementAsync(stockKey);
        //        return stock <= 0 ? 0 : stock;
        //    });
        //}

        /// <summary>
        /// 获取商品库存
        /// </summary>
        /// <param name="productId"></param>
        /// <returns></returns>
        public async Task<long> GetInventoryAsync(string productId)
        {
            var db = _redis.GetDatabase();
            string stockKey = string.Format(SeckillConst.SeckillProductStockKey, productId);
            var value = await db.StringGetAsync(stockKey);
            if (value.IsNull) return -1;
            return (long)value;
        }


        /**
         * 检查用户是否已经秒杀过该商品
         * @param userId 用户ID
         * @param productId 商品ID
         * @return 是否可以秒杀
         */
        public async Task<bool> CanUserSeckillAsync(string userId, string productId)
        {
            // 使用熔断策略包裹
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                string userSeckillResultKey = string.Format(SeckillConst.SeckillResultKey, userId, productId);
                var result = await db.StringGetAsync(userSeckillResultKey);
                return result.IsNullOrEmpty;  // 如果返回 null，表示用户没有秒杀过
            });
        }

        /**
         * 保存用户秒杀结果
         * @param userId 用户ID
         * @param productId 商品ID
         * @return 是否可以秒杀
         */
        public async Task<bool> SetUserSeckillResultAsync(string userId, string productId)
        {
            // 使用熔断策略包裹
            return await _circuitBreakerPolicy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                string userSeckillResultKey = string.Format(SeckillConst.SeckillResultKey, userId, productId);
                bool setResult = await db.StringSetAsync(userSeckillResultKey, "true", TimeSpan.FromMinutes(60));
                return setResult;
            });
        }

        /// <summary>
        /// 重试
        /// </summary>
        /// <returns></returns>
        public async Task<bool> RetryAsync()
        {
            try
            {
                // 通过熔断器执行检查
                return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                {
                    var db = _redis.GetDatabase();

                    // 发送PING命令检测连接
                    var response = await db.PingAsync();

                    // 响应时间小于1秒视为健康
                    return response.TotalMilliseconds < 1000;
                });
            }
            catch (BrokenCircuitException)  // 熔断器打开状态
            {
                _logger.LogInformation("[熔断拦截] 拒绝访问Redis");
                return false;
            }
            catch (Exception ex)  // 其他异常
            {
                _logger.LogInformation(ex, "[Redis检查失败] {message}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 可选：定期运行的健康检查
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task RunPeriodicHealthCheckAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _logger.LogInformation($"[健康检查] Redis状态: {(IsRedisAvailable ? "健康" : "故障")}");

                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        private void SetRedisStatus(bool isAvailable)
        {
            if (_isRedisAvailable != isAvailable)
            {
                _isRedisAvailable = isAvailable;
                _logger.LogInformation($"[状态变更] Redis服务: {(isAvailable ? "已恢复" : "不可用")}");

                // 这里可以添加通知逻辑（邮件/短信/日志等）
                // if (!isAvailable) NotifyServiceTeam();
            }
        }
    }
}
