using RabbitMQ.Client;

namespace Seckill_dotnet.RabbitMQ
{
    public interface IRabbitMQConnection : IDisposable
    {
        Task<IChannel> CreateChannel();
    }
}
