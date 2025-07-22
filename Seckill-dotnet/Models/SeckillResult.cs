namespace Seckill_dotnet.Models
{
    public class SeckillResult
    {
        public string? Message { get; set; }

        public int Code { get; set; }

        public string? OrderId { get; set; }


        public static SeckillResult Success(string message = "秒杀成功")
        {
            return new SeckillResult
            {
                Message = message,
                Code = 200
            };
        }

        public static SeckillResult Success(string orderId, string message = "秒杀成功")
        {
            return new SeckillResult
            {
                Message = message,
                Code = 200,
                OrderId = orderId
            };
        }

        public static SeckillResult Failure(string message = "秒杀失败")
        {
            return new SeckillResult
            {
                Message = message,
                Code = 500
            };
        }

        public static SeckillResult Failure(int code, string message = "秒杀失败")
        {
            return new SeckillResult
            {
                Message = message,
                Code = code
            };
        }
    }
}
