namespace Seckill_dotnet.Config
{
    public class RabbitMQOptions
    {
        public string HostName { get; set; }
        public int Port { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string VirtualHost { get; set; }

        public string Prefix { get; set; }

        public string QueueName { get; set; }
    }
}
