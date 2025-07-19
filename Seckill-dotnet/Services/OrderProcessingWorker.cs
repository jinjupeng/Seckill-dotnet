
using Seckill_dotnet.Models;
using Seckill_dotnet.RabbitMQ;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Seckill_dotnet.Services
{
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

                        // 消息确认
                        await channel.BasicAckAsync(deliveryTag: default, false, stoppingToken);
                    }, stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); // 添加延迟，避免空循环消耗CPU资源
                }
                catch (Exception ex)
                {
                    // 异常处理（记录日志等）
                    _logger.LogError(ex, "Error processing orders");

                    // 异常后延迟重试
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }

            }
            // 服务停止日志
            _logger.LogInformation("OrderProcessingWorker stopped at: {}", DateTime.Now);
        }
    }
}
