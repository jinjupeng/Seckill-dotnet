namespace Seckill_dotnet.Redis
{
    /// <summary>
    /// Redis配置类
    /// </summary>
    public class RedisSettings
    {
        /// <summary>
        /// 默认Redis配置连接字符串
        /// </summary>
        public string DefaultConnection { get; set; } = string.Empty;

        /// <summary>
        /// 分布式Redis锁连接字符串数组
        /// </summary>
        public string[] RedlockEndpoints { get; set; } = Array.Empty<string>();
        public RedlockSettings RedlockSettings { get; set; } = new();
    }
}
