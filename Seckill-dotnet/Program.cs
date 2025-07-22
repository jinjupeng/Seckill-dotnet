
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using RabbitMQ.Client;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using Seckill_dotnet.Config;
using Seckill_dotnet.Infrastructure;
using Seckill_dotnet.RabbitMQ;
using Seckill_dotnet.Redis;
using Seckill_dotnet.Services;
using StackExchange.Redis;

namespace Seckill_dotnet
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var env = builder.Environment;
            LogManager.Setup().LoadConfigurationFromFile($"nlog.config", optional: true);


            builder.Logging.ClearProviders();
            builder.Logging.AddNLog(); 
            builder.Services.AddNLog();

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 配置 MySQL 数据库
            builder.Services.AddDbContext<SeckillContext>(options =>
                options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")));

            // 配置 Redis 连接
            var redisConnection = builder.Configuration.GetValue<string>("Redis:ConnectionString");
            var redis = ConnectionMultiplexer.Connect(redisConnection);
            builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

            // 添加Redis连接多路复用器
            builder.Services.AddSingleton<RedLockMultiplexer>(provider =>
                ConnectionMultiplexer.Connect(redisConnection));

            // 注册RedLock工厂（单例）
            builder.Services.AddSingleton<IDistributedLockFactory>(provider =>
            {
                var multiplexers = new List<RedLockMultiplexer>
                {
                    provider.GetRequiredService<RedLockMultiplexer>()
                };

                return RedLockFactory.Create(multiplexers);
            });


            // 添加 RabbitMQ 配置
            builder.Services.Configure<RabbitMQOptions>(builder.Configuration.GetSection("RabbitMQ"));

            // 注册 RabbitMQ 连接工厂
            builder.Services.AddSingleton<IConnectionFactory>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<RabbitMQOptions>>().Value;
                return new ConnectionFactory
                {
                    HostName = settings.HostName,
                    Port = settings.Port,
                    UserName = settings.UserName,
                    Password = settings.Password,
                    VirtualHost = settings.VirtualHost,
                    
                };
            });

            // 添加RabbitMQService的服务注册
            builder.Services.AddSingleton<RabbitMQService>();
            builder.Services.AddSingleton<RabbitMQConnection>();
            builder.Services.AddSingleton<RabbitMqChannelManager>();

            builder.Services.AddHostedService<OrderProcessingWorker>(); // 后台服务消费者，模拟订单处理
            builder.Services.AddHostedService<InventorySyncService>(); // 后台服务，Redis库存同步

            builder.Services.AddScoped<SeckillService>();
            builder.Services.AddScoped<InventoryService>();
            builder.Services.AddScoped<OrderService>();

            builder.Services.AddSingleton<RedisService>();

            // 接口限流操作
            builder.Services.AddRateLimiter(options => {
                options.AddFixedWindowLimiter("seckill", opt => {
                    opt.PermitLimit = 1000; // 每秒1000请求
                    opt.Window = TimeSpan.FromSeconds(1);
                });
            });

            // 熔断降级策略

            // 健康检查
            builder.Services.AddHealthChecks()
                .AddCheck<RabbitMQHealthCheck>("rabbitmq")
                .AddCheck<RedisHealthCheck>("redis");

            var app = builder.Build();

            // 应用数据库迁移
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    var dbContext = services.GetRequiredService<SeckillContext>();

                    // 检查是否需要迁移
                    var pendingMigrations = dbContext.Database.GetPendingMigrations();
                    if (pendingMigrations.Any())
                    {
                        logger.LogInformation("应用 {Count} 个挂起的数据库迁移...", pendingMigrations.Count());
                        dbContext.Database.Migrate();
                        logger.LogInformation("数据库迁移完成");
                    }
                    else
                    {
                        logger.LogInformation("没有挂起的数据库迁移");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "数据库迁移过程中发生错误");

                    // 生产环境中可能需要终止应用
                    if (app.Environment.IsProduction())
                    {
                        //throw;
                    }
                }
            }


            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();
            app.MapHealthChecks("/health");

            app.MapControllers();

            app.Run();
        }
    }
}
