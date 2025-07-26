using Binance.Net.Enums;
using BotTradingCrypto.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Binance.Net.Clients;
using Binance.Net.Objects;
using CryptoExchange.Net.Authentication;
using Binance.Net.Objects.Options;
using CryptoExchange.Net.SharedApis;
using Binance.Net.Interfaces.Clients;
using Binance.Net;
using System.Collections.Concurrent;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects.Sockets;
using Binance.Net.Objects.Models.Spot;
using Binance.Net.Objects.Models.Spot.Socket;
using BotTradingCryto;
using Microsoft.Extensions.Caching.Memory;



namespace BotTradingCrypto.Infrastructure.Services
{
    public class BinanceService : IBinanceService
    {
        private readonly IBinanceRestClient _restClient;
        private readonly IBinanceSocketClient _socketClient;
        public readonly IMemoryCache _cache;
        private readonly SemaphoreSlim _throttle = new(1, 1);
        private int? _userDataSubscriptionId;
        private string? _listenKey;
        private CancellationTokenSource? _keepAliveCts;
        public BinanceService(
            IBinanceRestClient restClient, 
            IBinanceSocketClient socketClient,
            IMemoryCache cache)
        {
            _restClient = restClient;
            _socketClient = socketClient;
            _cache = cache;
            //KeepAliveUserStreamAsync().ConfigureAwait(false); // Start the keep-alive task
        }
        public async Task<OperationResult> GetSymbolInfoAsync(string symbol)
        {
            var symbolInfoResult = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync(symbol);
            if (symbolInfoResult.Success)
            {
                var infor = symbolInfoResult.Data.Symbols.FirstOrDefault();
                OperationResult result = OperationResult.Success;
                result.Data = infor;
                return result;
            }
            else
            {
                return OperationResult.Failed(null, "Failed to fetch symbol info");
            }
        }
        public async Task<OperationResult> PlaceSpotLimitOrderAsync(string symbol, decimal price, decimal quantity, bool isBuy)
        {
            var sharedRestSpot = _restClient.SpotApi.SharedClient;
            var side = isBuy ? OrderSide.Buy : OrderSide.Sell;
            var orderResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol, 
                side: isBuy? OrderSide.Buy: OrderSide.Sell, 
                type: SpotOrderType.Limit, 
                quantity,
                price
                );

            if (orderResult.Success)
            {
                OperationResult result = OperationResult.Success;
                result.Data = orderResult.Data.Id;
                Console.WriteLine($"Order executed: {side} {quantity} {symbol} at market price.");
                return result;
            }
            else
            {
                Console.WriteLine($"Order failed: {orderResult.Error}");
                return OperationResult.Failed(null, "Order failed");
            }
        }

        public async Task<double> GetCurrentPriceAsync(string symbol)
        {
            var priceResult = await _restClient.SpotApi.ExchangeData.GetPriceAsync(symbol);
            if (priceResult.Success)
            {
                return (double)priceResult.Data.Price;
            }
            else
            {
                throw new Exception("Failed to fetch current price from Binance.");
            }
        }
        public async Task<double> GetTradingFeeAsynce(string symbol)
        {
            try
            {
                var fee = await _restClient.SpotApi.Account.GetTradeFeeAsync(symbol);
                return (double)(fee.Data.FirstOrDefault()?.MakerFee??0);
            }
            catch (Exception ex)
            {
                return 0;
            }
        }
        public async Task ConnectSocketTradingAsync(string symbol, int num)
        {
            Console.WriteLine($"Subscribing to {symbol} trade updates...");
            var subscriptionResult = await _socketClient.SpotApi.ExchangeData.SubscribeToMiniTickerUpdatesAsync(
                symbol,
                data =>
                {
                    Console.WriteLine($"{DateTime.Now} Current price {num}: {data.Data.LastPrice}");
                });
            var dataSubscription = subscriptionResult.Data;
            dataSubscription.ConnectionLost += () => Console.WriteLine("Connection lost");
            dataSubscription.ConnectionRestored += (x) => Console.WriteLine("Connection restored");
            var subId = subscriptionResult.Data.Id;
            var socketId = subscriptionResult.Data.SocketId;
            var connections = _socketClient.SpotApi.CurrentConnections;
            Console.WriteLine($"Subscribed with ID: {subId} - socket {socketId} - amount connections {connections}");
            if (!subscriptionResult.Success)
            {
                Console.WriteLine($"Failed to subscribe: {subscriptionResult.Error}");
            }
        }
        public async Task CancelAllOrderAsync()
        {
            //await _restClient.SpotApi.ExchangeData.().ConfigureAwait(false);
        }
        /// <summary>
        /// Subscribe to a symbol, returning a GUID you can use to unsubscribe.
        /// </summary>
        public async Task<int> SubscribeMiniTickerAsync(
            string symbol,
            Action<double, Guid> onData,
            Guid orderBookId,
            CancellationToken ct = default)
        {
            // throttle to 5 subs/sec
            await _throttle.WaitAsync(ct);
            try
            {
                var res = await _socketClient
                    .SpotApi
                    .ExchangeData
                    .SubscribeToMiniTickerUpdatesAsync(
                        symbol,
                        data => onData((double)data.Data.LastPrice, orderBookId)
                     )
                    .ConfigureAwait(false);

                if (!res.Success)
                    throw new Exception(res.Error?.Message ?? "Subscription failed");

                return  res.Data.Id;
            }
            finally
            {
                // release after 250 ms
                _ = Task.Delay(250, ct).ContinueWith(_ => _throttle.Release());
            }
        }
        public async Task<OperationResult> SubscribeUserDataAsync(string symbol, Action<long> onData, Guid orderBookId, CancellationToken ct = default)
        {
            if (_userDataSubscriptionId.HasValue)
            {
                Console.WriteLine("Already subscribed to user data updates.");
                return OperationResult.Success;
            }
            await _throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var listenKeyResult = await _restClient.SpotApi.Account.StartUserStreamAsync();
                if (!listenKeyResult.Success)
                {
                    throw new Exception(listenKeyResult.Error?.Message ?? "Failed to start user stream");
                }
                _listenKey = listenKeyResult.Data.ToString()??"";

                var res = await _socketClient.SpotApi.Account.SubscribeToUserDataUpdatesAsync(
                    _listenKey,
                    onOrderUpdateMessage: data =>
                    {
                        var order = data.Data;
                        if (order.Status == OrderStatus.Filled && order.QuantityFilled == order.Quantity)
                        {
                            Console.WriteLine($"Order filled! Symbol: {order.Symbol}, OrderId: {order.Id}, Price: {order.Price}, Quantity: {order.Quantity}");
                            onData(order.Id); // Invoke the callback with the order ID
                        }
                    }
                );
                if (!res.Success)
                    throw new Exception(res.Error?.Message ?? "Subscription failed");
                else
                {
                    var dataSubscription = res.Data;
                    dataSubscription.ConnectionLost += () => Console.WriteLine("Connection lost");
                    dataSubscription.ConnectionRestored += (x) => Console.WriteLine("Connection restored");
                    _userDataSubscriptionId = res.Data.Id;
                    Console.WriteLine("Subscribed to user data updates.");
                    //await KeepAliveUserStreamAsync(ct).ConfigureAwait(false);
                }
                // Start keep-alive task
                _keepAliveCts = new CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    while (!_keepAliveCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(30), _keepAliveCts.Token);
                        if (_listenKey != null)
                        {
                            var keepAliveResult = await _restClient.SpotApi.Account.KeepAliveUserStreamAsync(_listenKey);
                            if (!keepAliveResult.Success)
                            {
                                Console.WriteLine("Failed to keep user stream alive: " + keepAliveResult.Error);
                            }
                        }
                    }
                }, _keepAliveCts.Token);
                return OperationResult.Success;
            }
            finally
            {
                // release after 250 ms
                _ = Task.Delay(250, ct).ContinueWith(_ => _throttle.Release());
            }
        }
        /// <summary>
        /// Unsubscribe a stream by the GUID returned at subscribe time.
        /// </summary>
        public async Task UnsubscribeMiniTickerAsync(int subscribeId)
        {
            await _socketClient.UnsubscribeAsync(subscribeId).ConfigureAwait(false);
        }
        public async Task UnsubscribeUserDataAsync()
        {
            if (_userDataSubscriptionId.HasValue)
            {
                await _socketClient.UnsubscribeAsync(_userDataSubscriptionId.Value).ConfigureAwait(false);
                _userDataSubscriptionId = null;
                Console.WriteLine("Unsubscribed from user data updates.");
            }
            else
            {
                Console.WriteLine("No active user data subscription to unsubscribe.");
            }
        }
        //public async Task KeepAliveUserStreamAsync(CancellationToken ct = default)
        //{
        //    var listenKeyResult = await _socketClient.SpotApi.Account.StartUserStreamAsync();
        //    if (!listenKeyResult.Success)
        //    {
        //        throw new Exception(listenKeyResult.Error?.Message ?? "Failed to start user stream");
        //    }
        //    var listenKey = listenKeyResult.Data.ToString() ?? "";
        //    _ = Task.Run(async () =>
        //    {
        //        while (!ct.IsCancellationRequested)
        //        {
        //            await Task.Delay(TimeSpan.FromMinutes(30), ct);
        //            var keepAliveResult = await _socketClient.SpotApi.Account.KeepAliveUserStreamAsync(listenKey);
        //            if (!keepAliveResult.Success)
        //            {
        //                Console.WriteLine("Failed to keep user stream alive: " + keepAliveResult.Error);
        //            }
        //        }
        //    }, ct);
        //}

        public async Task<int> GetTickSize(string symbol)
        {
            var symbolInfor = await _cache.GetOrCreateAsync(
                $"symbolInfo_{symbol}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(5);
                    var result = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync(symbol);
                    if (result.Success)
                    {
                        return result.Data.Symbols.FirstOrDefault();
                    }
                    else
                    {
                        throw new Exception("Failed to fetch symbol info");
                    }
                });
            var tickSize = symbolInfor?.PriceFilter?.TickSize ?? 0.01m; // Default tick size if not found
            var tickDecimal = tickSize.ToString().Split('.').Last().Length;
            return tickDecimal;
        }
        public async Task<int> GetStepSize(string symbol)
        {
            var symbolInfor = await _cache.GetOrCreateAsync(
                $"symbolInfo_{symbol}",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(5);
                    var result = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync(symbol);
                    if (result.Success)
                    {
                        return result.Data.Symbols.FirstOrDefault();
                    }
                    else
                    {
                        throw new Exception("Failed to fetch symbol info");
                    }
                });
            var stepSize = symbolInfor?.LotSizeFilter?.StepSize ?? 0.00001m; // Default tick size if not found
            var stepDecimal = stepSize.ToString().Split('.').Last().Length;
            return stepDecimal;
        }

        /// <summary>
        /// On reconnect, re-open all existing subscriptions.
        /// </summary>
        //private async Task ResubscribeAllAsync()
        //{
        //    var entries = _subs.ToArray();
        //    _subs.Clear();

        //    foreach (var (id, oldSub) in entries)
        //    {
        //        try
        //        {
        //            // Re-subscribe with the same symbol and no-op handler
        //            var res = await _socketClient
        //                .SpotApi
        //                .ExchangeData
        //                .SubscribeToTradeUpdatesAsync(
        //                    oldSub.Parameters[0],
        //                    _ => Task.CompletedTask)
        //                .ConfigureAwait(false);

        //            if (res.Success)
        //                _subs[id] = res.Data;
        //        }
        //        catch
        //        {
        //            // swallow and retry later or log
        //        }
        //    }
        //}

    }
}
