using Microsoft.AspNetCore.Mvc;
using Seckill_dotnet.Models;
using Seckill_dotnet.Redis;
using Seckill_dotnet.Services;

namespace Seckill_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SeckillController : ControllerBase
    {
        private readonly SeckillService _seckillService;
        private readonly RedisService _redisService;
        private readonly OrderService _orderService;

        public SeckillController(SeckillService seckillService, RedisService redisService, OrderService orderService)
        {
            _seckillService = seckillService;
            _redisService = redisService;
            _orderService = orderService;
        }

        /// <summary>
        /// 由于秒杀系统的高并发特性，通常会将库存数据存储在缓存中
        /// 这里为了测试，库存数据由接口同步到缓存和数据库
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("InitStock")]
        public async Task<IActionResult> InitStock([FromBody] InitStockRequest request)
        {
            // 1. 验证用户身份（JWT等）

            if (string.IsNullOrEmpty(request.ProductId))
            {
                return BadRequest("产品ID不能为空");
            }
            if (request.Stock <= 0)
            {
                return BadRequest("商品库存不能为0或负数");
            }
            
            await _redisService.PreheatInventoryAsync(request.ProductId, request.Stock);
            await _orderService.InitializeProductStockAsync(request.ProductId, request.Stock);
            return Ok($"初始化库存，产品ID = {request.ProductId}，产品库存 = {request.Stock}");
        }

        [HttpPost("Order")]
        public async Task<IActionResult> Order([FromBody] SeckillRequest request)
        {
            // 1. 验证用户身份（JWT等）

            request.UserId = new Random().Next().ToString();
            request.ProductId = "seckill_product_test";
            var result = await _seckillService.ProcessSeckillAsync(request.UserId, request.ProductId);
            return Ok(result);
        }
    }
}
