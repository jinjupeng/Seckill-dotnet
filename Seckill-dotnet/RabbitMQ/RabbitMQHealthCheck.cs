using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Seckill_dotnet.RabbitMQ
{

    public class RabbitMQHealthCheck : IHealthCheck
    {
        private readonly RabbitMQConnection _connection;
        public RabbitMQHealthCheck(RabbitMQConnection connection)
        {
            _connection = connection;
        }
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_connection.IsHealthy())
                return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ连接正常"));
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ连接不可用"));
        }
    }
}
