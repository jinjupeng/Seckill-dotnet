using Seckill_dotnet.Models;
using System.Text.Json;

namespace Seckill_dotnet.Middlewares
{
    /// <summary>
    /// 统一异常处理
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="next"></param>
        public ExceptionMiddleware(ILogger<ExceptionMiddleware> logger, RequestDelegate next)
        {
            _logger = logger;
            this._next = next;
        }

        /// <summary>
        /// 异常处理
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next.Invoke(context);
            }
            catch (Exception e)
            {
                await HandleExceptionAsync(context, e);
            }
        }

        /// <summary>
        /// 处理异常
        /// </summary>
        /// <param name="context"></param>
        /// <param name="exception"></param>
        /// <returns></returns>
        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, "【异常信息】：{message} \r\n【堆栈调用】：{stackTrace}", exception.Message, exception.StackTrace);

            // 1. 设置响应头前先检查是否已发送响应
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/json; charset=utf-8";
            }
            SeckillResult msgModel = SeckillResult.Failure("秒杀失败，请稍候重试！");

            await context.Response.WriteAsync(JsonSerializer.Serialize(msgModel));
        }

    }
}
