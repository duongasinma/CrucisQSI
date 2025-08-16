using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using Microsoft.AspNetCore.Mvc;
using Serilog;
namespace BotTradingCrypto.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TradingController : ControllerBase
    {
      
        private readonly ISpotGridTradingService _gridTradingService;
        private readonly IOrderBookStore _orderBookStore;
        private readonly IBinanceService _binanceService;
        private readonly ILogger<TradingController> _logger;

        public TradingController(ILogger<TradingController> logger, 
            ISpotGridTradingService gridTradingService,
            IOrderBookStore orderBookStore,
            IBinanceService binanceService)
        {
            _logger = logger;
            _gridTradingService = gridTradingService;
            _orderBookStore = orderBookStore;
            _binanceService = binanceService;
        }
        [HttpGet("GetAllOrder")]
        public async Task<IActionResult> Test()
        {
            var data = await _orderBookStore.GetAllOrderBook();
            _logger.LogWarning("Retrieved {Count} order books", data.Count());
            return Ok(data);
        }
      
        [HttpGet("CanceldAll")]
        public async Task<IActionResult> CanceldAll()
        {
            try
            {
                await _binanceService.CancelAllOrderAsync("TRUMPUSDT");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling all orders");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpGet("TrackingPrice")]
        public async Task<IActionResult> TrackingPrice([FromQuery]string symbol)
        {
            try
            {
                await _binanceService.TrackingTickerAsync(symbol);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking price for symbol {Symbol}");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("UnsubscribeMiniTicker")]
        public async Task<IActionResult> UnsubscribeMiniTickerAsync([FromQuery] string Id)
        {
            try
            {
                await _binanceService.UnsubscribeMiniTickerAsync(int.Parse(Id));
                return Ok("Unsubscribed successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpPost("StartBot")]
        public async Task<IActionResult> StartBot([FromBody]OrderBookDetail orderBookDetail, [FromQuery] string symbol)
        {
            try
            {
                // "BTCUSDT"
                await _gridTradingService.StartGridTradingAsync(symbol, orderBookDetail);
                return Ok("Grid trading started successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting grid trading");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpGet("GetTickSize")]
        public async Task<IActionResult> GetTickSize([FromQuery] string symbol)
        {
            try
            {
                var tickSize = await _binanceService.GetTickSize(symbol);
                return Ok(new { TickSize = tickSize });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tick size for symbol {Symbol}", symbol);
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpGet("GetStepSize")]
        public async Task<IActionResult> GetStepSize([FromQuery] string symbol)
        {
            try
            {
                var stepSize = await _binanceService.GetStepSize(symbol);
                return Ok(new { StepSize = stepSize });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tick size for symbol {Symbol}", symbol);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
