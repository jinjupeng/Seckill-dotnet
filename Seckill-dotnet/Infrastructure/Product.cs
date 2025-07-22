namespace Seckill_dotnet.Infrastructure
{
    public class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime LastSyncTime { get; set; }
        public int Stock { get; set; }  // 商品库存
        public int Version { get; set; }  // 乐观锁版本号

    }
}
