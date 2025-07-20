using Polly;
using Polly.CircuitBreaker;
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
        public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
        {
            _redis = redis;
            _logger = logger;
            // 定义熔断策略：当连续5次失败，熔断30秒
            _circuitBreakerPolicy = Policy
                .Handle<RedisException>()
                .Or<TimeoutException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (ex, breakDelay) =>
                    {
                        _logger.LogWarning($"Redis熔断器开启，熔断时间：{breakDelay.TotalSeconds}秒。");
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Redis熔断器重置。");
                    },
                    onHalfOpen: () =>
                    {
                        _logger.LogInformation("Redis熔断器半开启，尝试恢复。");
                    }
                );
        }

        /// <summary>
        /// 预热库存，将商品库存预设到Redis中
        /// </summary>
        /// <param name="productId"></param>
        /// <param name="stock"></param>
        /// <returns></returns>
        public async Task<bool> PreheatInventoryAsync(string productId, int stock)
        {
            var db = _redis.GetDatabase();
            return await db.StringSetAsync($"product_stock:{productId}", stock);
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
                // 使用Lua脚本原子性地检查库存并减少，当库存大于0时减少，否则返回-1
                var script = LuaScript.Prepare(
                    "local stock = tonumber(redis.call('get', KEYS[1])) " +
                    "if stock and stock > 0 then " +
                    "   return redis.call('decr', KEYS[1]) " +
                    "else " +
                    "   return -1 " +
                    "end"
                );
                var result = await db.ScriptEvaluateAsync(script, new RedisKey[] { $"product_stock:{productId}" });
                return (long)result;
            });
        }

        /// <summary>
        /// 获取商品库存
        /// </summary>
        /// <param name="productId"></param>
        /// <returns></returns>
        public async Task<long> GetInventoryAsync(string productId)
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync($"product_stock:{productId}");
            if (value.IsNull) return -1;
            return (long)value;
        }
    }
}
