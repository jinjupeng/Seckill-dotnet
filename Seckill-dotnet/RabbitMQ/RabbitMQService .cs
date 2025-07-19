using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace Seckill_dotnet.RabbitMQ
{
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
            using var channel = await _connection.CreateChannel();
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
                    // 消息确认，手动 ACK
                    await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                }
                catch(Exception ex)
                {
                    // 处理失败，拒绝消息并重新入队
                    //await channel.BasicNackAsync(ea.DeliveryTag, false, true);

                    // 处理失败，拒绝消息并放入死信队列
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false);

                    // 记录日志
                    _logger.LogError("消息队列接收消息出现异常：{}，{}", ex.Message, ex.StackTrace);
                }
            };
            await channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
            // Prevent the method from returning immediately
            //await Task.Delay(-1, cancellationToken);
        }
    }
}
