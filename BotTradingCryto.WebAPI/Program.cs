using Binance.Net.Clients;
using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using BotTradingCrypto.Infrastructure.Services;
using BotTradingCrypto.Infrastructure;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using MongoDB.Bson;
//using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
//builder.Services.Configure<KestrelServerOptions>(options =>
//{
//    options.Limits.MaxConcurrentConnections = 3; // Set the max concurrent connections
//});
builder.Services.Configure<GridConfiguration>(builder.Configuration.GetSection("GridConfig"));
builder.Services.AddBinance(builder.Configuration.GetSection("BinanceOptions"));

//Add mongo db
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));
builder.Services.AddSingleton<IMongoClient>(_ => {
    var connectionString =
        builder
            .Configuration
            .GetSection("MongoDbSettings:ConnectionString")?
            .Value;
    var settings = MongoClientSettings.FromConnectionString(connectionString);
    settings.ServerApi = new ServerApi(ServerApiVersion.V1);

    return new MongoClient(connectionString);
});
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<LockProvider>();
builder.Services.AddSingleton<IBinanceService, BinanceService>();
builder.Services.AddScoped<ISpotGridTradingService, SpotGridTradingService>();
builder.Services.AddScoped<IOrderBookStore, OrderBookStore>();

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
