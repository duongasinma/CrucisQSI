using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot;
using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using CryptoExchange.Net.Interfaces;
using Microsoft.AspNetCore.Mvc;
using static System.Net.Mime.MediaTypeNames;

namespace BotTradingCrypto.WebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };
        private readonly ISpotGridTradingService _gridTradingService;
        private readonly IOrderBookStore _orderBookStore;
        private readonly IBinanceService _binanceService;
        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, 
            ISpotGridTradingService gridTradingService,
            IOrderBookStore orderBookStore,
            IBinanceService binanceService)
        {
            _logger = logger;
            _gridTradingService = gridTradingService;
            _orderBookStore = orderBookStore;
            _binanceService = binanceService;
        }
        [HttpGet("Test")]
        public async Task<IActionResult> Test()
        {
            var data = await _orderBookStore.GetAllOrderBook();
            var rs = await _binanceService.GetAccoutInfoAsync();
            if (rs.Succeeded && rs.Data is BinanceAccountInfo accountInfo)
            {
                BinanceAccountInfo balance = accountInfo;
                var btcBalance = balance.Balances.FirstOrDefault(b => b.Asset == "BTC");
                var usdtBalance = balance.Balances.FirstOrDefault(b => b.Asset == "USDT");
                return Ok(new {btcBalance, usdtBalance }); 
            }
            _logger.LogInformation("Test endpoint hit at {Time}", DateTime.UtcNow);
            return Ok(data);
        }
      
        [HttpGet("CanceldAll")]
        public async Task<IActionResult> CanceldAll()
        {
            try
            {
                await _binanceService.CancelAllOrderAsync("BTCUSDT");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling all orders");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpPost("StartBot")]
        public async Task<IActionResult> StartBot([FromBody]OrderBookDetail orderBookDetail)
        {
            try
            {
                await _gridTradingService.StartGridTradingAsync("BTCUSDT", orderBookDetail);
                return Ok("Grid trading started successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting grid trading");
                return StatusCode(500, "Internal server error");
            }
        }
        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
}
