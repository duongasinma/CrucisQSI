// See https://aka.ms/new-console-template for more information
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using CryptoClients.Net;
using CryptoClients.Net.Enums;
using CryptoExchange.Net.SharedApis;
using Kucoin.Net.Clients;
using Kucoin.Net.Interfaces.Clients;
//var restClient = new ExchangeRestClient(new ExchangeRes
//{
//    Environment = ExchangeEnvironment.Testnet
//});
IBinanceRestClient binanceRestClient = new BinanceRestClient();
IKucoinSocketClient kucoinSocketClient = new KucoinSocketClient();
var socketClient = new ExchangeSocketClient();

var symbol = new SharedSymbol(TradingMode.Spot, "BTC", "USDT");
var updateSubscriptions = await socketClient.SubscribeToTradeUpdatesAsync(
    new SubscribeTradeRequest(symbol),
    update =>
    {
        Console.WriteLine($"{update.Exchange} - First Trade: {update.Data.First().Price} - {update.Data.First().Quantity}");
    },
    [Exchange.Binance, Exchange.Bitget]);
//var subscriptions = await client.SubscribeToTradeUpdatesAsync(new SubscribeTradeRequest(symbol), updateSubscriptions =>
//{
//    Console.WriteLine($"{updateSubscriptions.Exchange} - Seconde Trade: {updateSubscriptions.Data.First().Price}");
//}, [Exchange.Binance, Exchange.Bitget]);
Console.ReadLine();