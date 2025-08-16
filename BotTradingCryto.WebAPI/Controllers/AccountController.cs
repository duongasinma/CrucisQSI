using Binance.Net.Objects.Models.Spot;
using BotTradingCrypto.Application;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace BotTradingCrypto.WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly IBinanceService _binanceService;
        private readonly ILogger<AccountController> _logger;
        public AccountController(IBinanceService binanceService, ILogger<AccountController> logger)
        {
            _binanceService = binanceService;
            _logger = logger;
        }

        [HttpGet]
        public async Task< IActionResult> GetAccountDetails()
        {
            var rs = await _binanceService.GetAccoutInfoAsync();
            if (rs.Succeeded && rs.Data is BinanceAccountInfo accountInfo)
            {
                BinanceAccountInfo balance = accountInfo;
                var btcBalance = balance.Balances.FirstOrDefault(b => b.Asset == "TRUMP");
                var usdtBalance = balance.Balances.FirstOrDefault(b => b.Asset == "USDT");
                return Ok(new { btcBalance, usdtBalance });
            }
            return Ok(new { Message = "Account details retrieved successfully." });
        }
        
        [HttpGet("TradingFee")]
        public async Task<IActionResult> GetTradingFee(string symbol)
        {
            var rs = await _binanceService.GetTradingFeeAsynce(symbol);
            return Ok(new {Data = rs, Message = "Trading fee retrieved successfully." });
        }

        /// <summary>
        /// Check system clock synchronization with Binance server
        /// </summary>
        [HttpGet("check-clock-sync")]
        public async Task<IActionResult> CheckClockSync()
        {
            try
            {
                var result = await _binanceService.CheckSystemClockSynchronizationAsync();
                
                if (result.Succeeded)
                {
                    return Ok(new
                    {
                        Success = true,
                        Message = result.Message,
                        Data = result.Data
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = result.Message,
                        Data = result.Data,
                        Recommendation = "Please synchronize your system clock with NTP servers"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking clock synchronization");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Internal server error while checking clock synchronization",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Test account access and diagnose signature issues
        /// </summary>
        [HttpGet("test-access")]
        public async Task<IActionResult> TestAccountAccess()
        {
            try
            {
                var result = await _binanceService.TestAccountAccessAsync();
                
                if (result.Succeeded)
                {
                    return Ok(new
                    {
                        Success = true,
                        Message = result.Message,
                        Data = result.Data
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        Success = false,
                        Message = result.Message,
                        Data = result.Data,
                        Recommendations = new[]
                        {
                            "Check API Key and Secret Key configuration",
                            "Ensure system clock is synchronized with NTP servers",
                            "Verify API Key permissions (Spot Trading required)",
                            "Check if IP address is whitelisted in Binance API settings",
                            "Confirm using correct environment (testnet vs mainnet)"
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing account access");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Internal server error while testing account access",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get comprehensive diagnostic information
        /// </summary>
        [HttpGet("diagnostics")]
        public async Task<IActionResult> GetDiagnostics()
        {
            try
            {
                var clockSync = await _binanceService.CheckSystemClockSynchronizationAsync();
                var accountTest = await _binanceService.TestAccountAccessAsync();
                
                return Ok(new
                {
                    ClockSynchronization = new
                    {
                        Success = clockSync.Succeeded,
                        Message = clockSync.Message,
                        Data = clockSync.Data
                    },
                    AccountAccess = new
                    {
                        Success = accountTest.Succeeded,
                        Message = accountTest.Message,
                        Data = accountTest.Data
                    },
                    OverallStatus = clockSync.Succeeded && accountTest.Succeeded ? "OK" : "Issues Detected",
                    Timestamp = DateTimeOffset.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running diagnostics");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Internal server error while running diagnostics",
                    Error = ex.Message
                });
            }
        }
    }
}
