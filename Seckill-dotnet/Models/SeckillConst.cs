namespace Seckill_dotnet.Models
{
    public static class SeckillConst
    {
        public const string SeckillLockResourceId = "Seckill_Lock:ProductId:{0}"; // Redis锁-ResourceId

        public const string SeckillProductStockKey = "Seckill_Product_Stock:ProductId:{0}"; // Redis库存key

        public const string SeckillResultKey = "Seckill_Result:User:{0}:ProductId:{1}"; // Redis秒杀结果key

    }
}
