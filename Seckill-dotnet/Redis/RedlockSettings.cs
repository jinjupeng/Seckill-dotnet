namespace Seckill_dotnet.Redis
{
    public class RedlockSettings
    {
        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 锁过期时间 (毫秒)
        /// </summary>
        public int ExpiryTime { get; set; } = 300;

        /// <summary>
        /// 获取锁等待时间 (毫秒)
        /// </summary>
        public int WaitTime { get; set; } = 100;

        /// <summary>
        /// 重试间隔 (毫秒)
        /// </summary>
        public int RetryDelay { get; set; } = 50;
    }
}
