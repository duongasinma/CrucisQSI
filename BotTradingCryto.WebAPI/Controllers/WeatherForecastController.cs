using Binance.Net.Clients;
using Binance.Net.Enums;
using BotTradingCrypto.Application;
using CryptoExchange.Net.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BotTradingCryto.WebAPI.Controllers
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
        private readonly ILogger<WeatherForecastController> _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, 
            ISpotGridTradingService gridTradingService
            )
        {
            _logger = logger;
            _gridTradingService = gridTradingService;
        }
        [HttpGet("Test")]
        public async Task<IActionResult> Test()
        {
            await _gridTradingService.StartGridTradingAsync("BTCUSDT");
            _logger.LogInformation("Test endpoint hit at {Time}", DateTime.UtcNow);
            return Ok("Test successful");
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
