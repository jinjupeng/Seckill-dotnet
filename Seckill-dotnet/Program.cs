
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
using Seckill_dotnet.Middlewares;
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

            // ���� MySQL ���ݿ�
            builder.Services.AddDbContext<SeckillContext>(options =>
                options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Redis������
            var redisSection = builder.Configuration.GetSection("Redis");
            var redisSettings = redisSection.Get<RedisSettings>();
            if (redisSettings?.RedlockEndpoints == null || redisSettings.RedlockEndpoints.Length == 0)
            {
                throw new InvalidOperationException("Redis Redlock endpoints not configured");
            }

            // 1. ���� StackExchange.Redis ��Ϊ�� Redis �ͻ���
            builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
                ConnectionMultiplexer.Connect(redisSettings.DefaultConnection));


            // 2. ���� Redlock.NET ʹ�ö�� Redis �ڵ�
            builder.Services.AddSingleton<IDistributedLockFactory>(serviceProvider =>
            {
                // ���ö�� Redis �ڵ�
                List<RedLockMultiplexer> multiplexers = redisSettings.RedlockEndpoints.Select(endpoint =>
                {
                    var cfg = ConfigurationOptions.Parse(endpoint);
                    // ���Redis���Ӷ�·������
                    RedLockMultiplexer redLockMultiplexer = ConnectionMultiplexer.Connect(cfg);
                    return redLockMultiplexer;
                }).ToList();
                // ע��RedLock������������
                return RedLockFactory.Create(multiplexers, new RedLockRetryConfiguration(
                    retryCount: redisSettings.RedlockSettings.RetryCount,
                    retryDelayMs: redisSettings.RedlockSettings.RetryDelay
                ));
            });


            // ��� RabbitMQ ����
            builder.Services.Configure<RabbitMQOptions>(builder.Configuration.GetSection("RabbitMQ"));

            // ע�� RabbitMQ ���ӹ���
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

            // ���RabbitMQService�ķ���ע��
            builder.Services.AddSingleton<RabbitMQService>();
            builder.Services.AddSingleton<RabbitMQConnection>();
            builder.Services.AddSingleton<RabbitMqChannelManager>();

            builder.Services.AddHostedService<OrderProcessingWorker>(); // ��̨���������ߣ�ģ�ⶩ������
            builder.Services.AddHostedService<InventorySyncService>(); // ��̨����Redis���ͬ��

            builder.Services.AddScoped<SeckillService>();
            builder.Services.AddScoped<OrderService>();

            builder.Services.AddSingleton<RedisService>();

            // �ӿ���������
            builder.Services.AddRateLimiter(options => {
                options.AddFixedWindowLimiter("seckill", opt => {
                    opt.PermitLimit = 1000; // ÿ��1000����
                    opt.Window = TimeSpan.FromSeconds(1);
                });
            });

            // �۶Ͻ�������

            // �������
            builder.Services.AddHealthChecks()
                .AddCheck<RabbitMQHealthCheck>("rabbitmq")
                .AddCheck<RedisHealthCheck>("redis");

            var app = builder.Build();

            // Ӧ�����ݿ�Ǩ��
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    var dbContext = services.GetRequiredService<SeckillContext>();

                    // ����Ƿ���ҪǨ��
                    var pendingMigrations = dbContext.Database.GetPendingMigrations();
                    if (pendingMigrations.Any())
                    {
                        logger.LogInformation("Ӧ�� {Count} ����������ݿ�Ǩ��...", pendingMigrations.Count());
                        dbContext.Database.Migrate();
                        logger.LogInformation("���ݿ�Ǩ�����");
                    }
                    else
                    {
                        logger.LogInformation("û�й�������ݿ�Ǩ��");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "���ݿ�Ǩ�ƹ����з�������");

                    // ���������п�����Ҫ��ֹӦ��
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
            app.UseMiddleware<ExceptionMiddleware>(); // ȫ���쳣����

            app.UseAuthorization();
            app.MapHealthChecks("/health");

            app.MapControllers();

            app.Run();
        }
    }
}
