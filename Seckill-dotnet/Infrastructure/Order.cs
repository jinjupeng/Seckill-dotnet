namespace Seckill_dotnet.Infrastructure
{
    public class Order
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string ProductId { get; set; }

        public DateTime OrderTime { get; set; }
    }
}
