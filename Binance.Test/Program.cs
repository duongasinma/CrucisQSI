using System;
using System.Threading.Tasks;
using Binance.Net;
using Binance.Net.Enums;
using Binance.Net.Objects;
using CryptoExchange.Net.Authentication;
using Binance.Net.Interfaces;
using Binance.Net.Clients;
using Binance.Net.SymbolOrderBooks;

namespace BinanceGridTradingBotExample
{
    class Program
    {
        IBinanceTrackerFactory nas;
        // Flag to ensure we only place one order in this example.
        private static bool orderPlaced = false;

        static async Task Main(string[] args)
        {
            double a = 0.1;
            double b = 0.2;
            // Replace with your Binance API credentials.
            var apiKey = "sybWFxQa2pJlxwm9VeoNwVSCQlOAGV31fxFBj6rAl3QgzDctgeAd0dyQHO8bahu8";
            var apiSecret = "LeAS9bifY62wdqbHadDLYNQX8AASkVlRx5u9RrWXj5MXoTHndpEI38fs85gd3A4H";

            // Define your trading parameters.
            string symbol = "BTCUSDT";
            decimal targetBuyPrice = 107500; // Example target price
            decimal quantity = 0.001m;        // Trading quantity

            // Create a BinanceClient for REST API calls.
            //var clientOptions = new BinanceClientOptions
            //{
            //    ApiCredentials = new ApiCredentials(apiKey, apiSecret)
            //};
            using var client = new BinanceRestClient(options =>
            {
                options.ApiCredentials = new ApiCredentials(apiKey, apiSecret);
                options.Environment = BinanceEnvironment.Testnet; // Use Testnet for testing
                options.RateLimiterEnabled = true; // Enable rate limiting
            });
            var spotBook = new BinanceSpotSymbolOrderBook("ETHUSDT");
            var coinBook = new BinanceFuturesCoinSymbolOrderBook("ETHUSD");
            var usdtBook = new BinanceFuturesUsdtSymbolOrderBook("ETHUSDT");

            //var symbolInfo = (await client.SpotApi.ExchangeData.GetExchangeInfoAsync("ETHUSDT")).Data.Symbols.FirstOrDefault();
            //Console.WriteLine($"Symbol: {(symbolInfo.Filters.FirstOrDefault()).FilterType}");

            //var rs = client.SpotApi.ExchangeData.GetTickerAsync("ETHUSDT").Result;

            //var result = await client.SpotApi.Account.GetBalancesAsync();
            //var firstBalance = result.Data.First();

            //Console.WriteLine($"{firstBalance.Asset}: {firstBalance.Total}"); 4470834

            //----------- BUY ORDER EXAMPLE -----------------
            var result2 = await client.SpotApi.Trading.PlaceOrderAsync(
            "ETHUSDT",
            OrderSide.Buy,
            SpotOrderType.Market,
            1m
            //timeInForce: TimeInForce.GoodTillCanceled
            );
            Console.WriteLine(result2.Data.Id + " - order id");
            // Get ordrer
            var result = await client.SpotApi.Trading.GetOrderAsync("ETHUSDT", 2822949);
            Console.WriteLine(result.Data.Status);
            //Cancel order 
            //var resultCancel = await client.SpotApi.Trading.CancelOrderAsync("ETHUSDT", 4470834);
            //Console.WriteLine(result.Success);
            //---------------------------------------------


            #region Socket
            //------------ SOCKET CLIENT EXAMPLE --------------
            // Create a BinanceSocketClient for WebSocket connections.
            using var socketClient = new BinanceSocketClient(options =>
            {
                options.RateLimiterEnabled = true; // Enable rate limiting
                options.ConnectDelayAfterRateLimited = TimeSpan.FromSeconds(5); // Delay after being rate limited
            });
            Console.WriteLine($"Subscribing to {symbol} kline updates...");
            // Subscribe to kline updates (using a 1-minute interval for this example).
            //var subscriptionResult = await socketClient.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(
            //    symbol,
            //    KlineInterval.OneMinute,
            //    async klineData =>
            //    {
            //        // Using the closing price of the kline event.
            //        decimal currentPrice = klineData.Data.Data.ClosePrice;
            //        Console.WriteLine($"[{DateTime.Now}] Current Price for {symbol}: {currentPrice}");

            //        // Check if the target is reached and no order has been placed yet.
            //        if (!orderPlaced && currentPrice <= targetBuyPrice)
            //        {
            //            orderPlaced = true; // Prevent duplicate triggers
            //            Console.WriteLine($"Target reached: {currentPrice} <= {targetBuyPrice}. Placing limit buy order...");

            //            // Place a limit buy order using the REST API.
            //            var orderResult = await client.SpotApi.Trading.PlaceOrderAsync(
            //                symbol: symbol,
            //                side: OrderSide.Buy,
            //                type: SpotOrderType.Limit,
            //                quantity: quantity,
            //                price: targetBuyPrice,
            //                timeInForce: TimeInForce.GoodTillCanceled
            //            );

            //            if (orderResult.Success)
            //            {
            //                Console.WriteLine("Order placed successfully!");
            //            }
            //            else
            //            {
            //                Console.WriteLine($"Order placement failed: {orderResult.Error}");
            //            }
            //        }
            //    });

            //if (!subscriptionResult.Success)
            //{
            //    Console.WriteLine("Error subscribing to kline updates: " + subscriptionResult.Error);
            //    return;
            //}


            //var subscriptionResult = await socketClient.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(
            //    symbol,
            //    data =>
            //    {
            //        Console.WriteLine($"{data.Data.TradeTime} - Trade: {data.Data.Price} - {data.Data.Quantity}");
            //    });
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();

            // Unsubscribe when done.
            //await socketClient.UnsubscribeAsync(subscriptionResult.Data);
            #endregion Socket
        }
    }
}