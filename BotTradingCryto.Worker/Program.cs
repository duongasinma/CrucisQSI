using Binance.Net.Clients;
using BotTradingCrypto.Application;
using BotTradingCrypto.Infrastructure.Services;
using BotTradingCryto.Worker;
using CryptoExchange.Net.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxConcurrentConnections = 2; // Set the max concurrent connections
});


builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IBinanceService, BinanceService>();
builder.Services.AddSingleton<ISpotGridTradingService, SpotGridTradingService>();
// Configure Binance client
var socketClient = new BinanceSocketClient(options =>
{
    options.RateLimiterEnabled = true; // Enable rate limiting
    options.ConnectDelayAfterRateLimited = TimeSpan.FromSeconds(10); // Delay after being rate limited
    options.MaxSocketConnections = 2; // Set the maximum number of concurrent socket connections
});
//builder.Services.AddSingleton(socketClient);
var host = builder.Build();
host.Run();
