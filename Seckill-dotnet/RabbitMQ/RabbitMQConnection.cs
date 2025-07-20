using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Seckill_dotnet.RabbitMQ
{
    public class RabbitMQConnection : IDisposable
    {
        private readonly IConnectionFactory _factory;
        private IConnection _connection;
        private readonly ILogger<RabbitMQConnection> _logger;
        private readonly RabbitMqChannelManager _channelManager;
        private bool _disposed;

        public RabbitMQConnection(IConnectionFactory factory, ILogger<RabbitMQConnection> logger, RabbitMqChannelManager channelManager)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));

            _connection = _factory.CreateConnectionAsync().Result;
            RegisterConnectionEvents(_connection);
        }

        private void RegisterConnectionEvents(IConnection connection)
        {
            connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
            connection.ConnectionBlockedAsync += OnConnectionBlockedAsync;
            connection.ConnectionUnblockedAsync += OnConnectionUnblockedAsync;
        }

        private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs e)
        {
            _logger.LogWarning("RabbitMQ连接关闭: {ReplyText}", e.ReplyText);
            if (e.Initiator != ShutdownInitiator.Application)
            {
                // 异步重连
                _ = Task.Run(Reconnect);
            }
            return Task.CompletedTask;
        }

        private Task OnConnectionBlockedAsync(object sender, ConnectionBlockedEventArgs e)
        {
            _logger.LogWarning("连接被阻塞: {Reason}", e.Reason);
            return Task.CompletedTask;
        }

        private Task OnConnectionUnblockedAsync(object sender, AsyncEventArgs e)
        {
            _logger.LogInformation("连接解除阻塞");
            return Task.CompletedTask;
        }

        private async Task Reconnect()
        {
            int attempt = 0;
            while (!_disposed)
            {
                try
                {
                    attempt++;
                    _logger.LogInformation("尝试重新连接 (第{Attempt}次)", attempt);
                    _connection?.Dispose();
                    _connection = _factory.CreateConnectionAsync().Result;
                    RegisterConnectionEvents(_connection);
                    _logger.LogInformation("RabbitMQ重连成功");
                    break;
                }
                catch (Exception ex)
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
                    _logger.LogWarning("重连失败: {Message}，{Delay}秒后重试", ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _channelManager?.Dispose();
            try
            {
                _connection?.CloseAsync();
                _connection?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ连接关闭异常");
            }
            GC.SuppressFinalize(this);
        }

        public bool IsHealthy()
        {
            return _connection != null && _connection.IsOpen;
        }
    }
}