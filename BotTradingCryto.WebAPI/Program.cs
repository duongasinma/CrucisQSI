using Binance.Net.Clients;
using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using BotTradingCrypto.Infrastructure.Services;
using BotTradingCrypto.Infrastructure;
using MongoDB.Driver;
//using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//builder.Services.Configure<KestrelServerOptions>(options =>
//{
//    options.Limits.MaxConcurrentConnections = 3; // Set the max concurrent connections
//});
builder.Services.Configure<GridConfiguration>(builder.Configuration.GetSection("GridConfig"));
builder.Services.AddBinance(builder.Configuration.GetSection("BinanceOptions"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IBinanceService, BinanceService>();
builder.Services.AddScoped<ISpotGridTradingService, SpotGridTradingService>();
builder.Services.AddSingleton<IOrderBookStore, OrderBookStore>();

builder.Services.AddMemoryCache();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
