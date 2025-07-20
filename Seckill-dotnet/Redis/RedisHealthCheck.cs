namespace Seckill_dotnet.Redis
{
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using StackExchange.Redis;

    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisHealthCheck(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_redis.IsConnected)
                return Task.FromResult(HealthCheckResult.Healthy("Redis连接正常"));
            return Task.FromResult(HealthCheckResult.Unhealthy("Redis连接不可用"));
        }
    }
}
