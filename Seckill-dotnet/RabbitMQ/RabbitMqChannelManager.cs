using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace Seckill_dotnet.RabbitMQ
{
    public class RabbitMqChannelManager
    {
        private readonly IConnection _connection; 
        private readonly IConnectionFactory _factory;
        private readonly ConcurrentDictionary<string, IChannel> _channels = new ConcurrentDictionary<string, IChannel>();
        private readonly Timer _healthCheckTimer;
        private readonly ILogger<RabbitMqChannelManager> _logger;

        public RabbitMqChannelManager(IConnectionFactory factory, ILogger<RabbitMqChannelManager> logger)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _connection = _factory.CreateConnectionAsync().Result;
            _healthCheckTimer = new Timer(CheckChannelHealth, null, 0, 60_000); // 每分钟检查一次
            _logger = logger;
        }

        public async Task<IChannel> GetChannelForQueue(string queueName)
        {
            if (_channels.TryGetValue(queueName, out var channel) && channel.IsOpen)
                return channel;

            return await CreateNewChannel(queueName);
        }

        private async Task<IChannel> CreateNewChannel(string queueName)
        {
            var channel = await _connection.CreateChannelAsync();

            // 配置通道
            await channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // 设置QoS
            await channel.BasicQosAsync(0, 1, false);

            _channels[queueName] = channel;
            return channel;
        }

        private void CheckChannelHealth(object state)
        {
            foreach (var (queueName, channel) in _channels.ToArray())
            {
                if (!channel.IsOpen)
                {
                    _logger.LogWarning("通道已关闭，重新创建: {QueueName}", queueName);
                    _channels.TryRemove(queueName, out _);
                    CreateNewChannel(queueName).Wait(); // 同步创建（后台任务）
                }
            }
        }

        public void Dispose()
        {
            _healthCheckTimer?.Dispose();
            foreach (var channel in _channels.Values)
            {
                try
                {
                    channel.CloseAsync();
                    channel.Dispose();
                }
                catch { /* 忽略关闭异常 */ }
            }
            _channels.Clear();
        }
    }
}
