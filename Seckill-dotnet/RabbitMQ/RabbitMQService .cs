using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Seckill_dotnet.RabbitMQ
{
    public class RabbitMQService
    {
        private readonly ILogger<RabbitMQService> _logger;
        private readonly RabbitMqChannelManager _channelManager;
        private readonly ConcurrentDictionary<string, IChannel> _channels = new ConcurrentDictionary<string, IChannel>();

        public RabbitMQService(ILogger<RabbitMQService> logger, RabbitMqChannelManager channelManager)
        {
            _logger = logger;
            _channelManager = channelManager;
        }


        public async Task SendAsync(string exchange, string routingKey, object message, bool mandatory = false, CancellationToken cancellationToken = default)
        {

            try
            {
                var channel = await _channelManager.GetChannelForQueue(routingKey);
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
                _logger.LogWarning(ex, "RabbitMQ消息发送被取消");
                //throw; // 如需向上传递可取消注释
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ消息发送异常");
                //throw; // 如需向上传递可取消注释
            }
        }

        public async Task ReceiveAsync(string queueName, Func<IChannel, byte[], Task<bool>> callback, CancellationToken cancellationToken = default)
        {
            var channel = await _channelManager.GetChannelForQueue(queueName);

            // 声明队列
            await channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                try
                {
                    // 直接传递 model 和 body 给 callback，不需要转换
                    bool result = await callback(channel, body);
                    if (result) // 处理成功后才进行消息确认
                    {
                        await SafeAckAsync(channel, ea.DeliveryTag, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "消息处理失败");

                    // 安全拒绝消息
                    await SafeNackAsync(channel, ea.DeliveryTag, ex);
                }
            };
            await channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);


            // 注册取消令牌处理
            cancellationToken.Register(() => CleanupQueueChannel(queueName));
        }

        private async Task SafeAckAsync(IChannel channel, ulong deliveryTag, CancellationToken cancellationToken)
        {
            try
            {
                if (channel?.IsOpen == true)
                {
                    await channel.BasicAckAsync(deliveryTag, false, cancellationToken);
                }
            }
            catch (AlreadyClosedException ex)
            {
                _logger.LogWarning("通道已关闭，无法确认消息: {DeliveryTag}", deliveryTag);
                // 可在此处重试或记录消息状态
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("消息确认操作已取消");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "消息确认失败");
            }
        }

        private async Task SafeNackAsync(IChannel channel, ulong deliveryTag, Exception ex)
        {
            try
            {
                if (channel?.IsOpen == true)
                {
                    // 根据异常类型决定处理方式
                    if (IsRecoverableError(ex))
                    {
                        // 可恢复错误：重新入队
                        await channel.BasicNackAsync(deliveryTag, false, true);
                    }
                    else
                    {
                        // 不可恢复错误：进入死信队列
                        await channel.BasicNackAsync(deliveryTag, false, false);
                    }
                }
            }
            catch (AlreadyClosedException)
            {
                _logger.LogWarning("通道已关闭，无法拒绝消息");
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "拒绝消息失败");
            }
        }

        private static bool IsRecoverableError(Exception ex)
        {
            // 可恢复错误示例：数据库暂时不可用、网络短暂中断
            return ex is TimeoutException ||
                   //ex is SqlException sqlEx && sqlEx.Number == -2 || // 超时错误
                   ex is HttpRequestException ||
                   ex is SocketException;
        }

        private void CleanupQueueChannel(string queueName)
        {
            if (_channels.TryRemove(queueName, out var channel))
            {
                try
                {
                    channel.CloseAsync();
                    channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭通道失败: {QueueName}", queueName);
                }
            }
        }
    }
}
