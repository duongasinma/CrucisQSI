using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using BotTradingCrypto.Domain.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


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
        private readonly ILogger<BinanceService> _logger;
        public BinanceService(
            IBinanceRestClient restClient, 
            IBinanceSocketClient socketClient,
            IMemoryCache cache,
            ILogger<BinanceService> logger)
        {
            _restClient = restClient;
            _socketClient = socketClient;
            _cache = cache;
            _logger = logger;
            //KeepAliveUserStreamAsync().ConfigureAwait(false); // Start the keep-alive task
        }

        /// <summary>
        /// Check system clock synchronization with Binance server time
        /// </summary>
        public async Task<OperationResult> CheckSystemClockSynchronizationAsync()
        {
            try
            {
                _logger.LogInformation("[BinanceService] Checking system clock synchronization with Binance server...");
                
                // Get system time before API call
                var systemTimeBefore = DateTimeOffset.UtcNow;
                
                // Get Binance server time
                var serverTimeResult = await _restClient.SpotApi.ExchangeData.GetServerTimeAsync();
                
                // Get system time after API call
                var systemTimeAfter = DateTimeOffset.UtcNow;
                
                if (!serverTimeResult.Success)
                {
                    _logger.LogError($"[BinanceService] Failed to get server time: {serverTimeResult.Error?.Message}");
                    return OperationResult.Failed(null, "Failed to get Binance server time");
                }
                
                var serverTime = serverTimeResult.Data;
                
                // Calculate estimated network latency
                var networkLatency = (systemTimeAfter - systemTimeBefore).TotalMilliseconds / 2;
                
                // Adjust server time for network latency
                var adjustedServerTime = serverTime.AddMilliseconds(networkLatency);
                
                // Calculate time difference
                var timeDifference = Math.Abs((systemTimeAfter - adjustedServerTime).TotalMilliseconds);
                
                _logger.LogInformation($"[BinanceService] System Time: {systemTimeAfter:yyyy-MM-dd HH:mm:ss.fff} UTC");
                _logger.LogInformation($"[BinanceService] Server Time: {serverTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
                _logger.LogInformation($"[BinanceService] Network Latency: {networkLatency:F2} ms");
                _logger.LogInformation($"[BinanceService] Time Difference: {timeDifference:F2} ms");
                
                // Binance typically allows up to 5000ms time window
                var isInSync = timeDifference <= 3000; // Use 3000ms as safe threshold
                
                var result = new
                {
                    SystemTime = systemTimeAfter,
                    ServerTime = serverTime,
                    TimeDifferenceMs = timeDifference,
                    NetworkLatencyMs = networkLatency,
                    IsInSync = isInSync,
                    MaxAllowedDifferenceMs = 5000,
                    RecommendedMaxDifferenceMs = 3000
                };
                
                if (isInSync)
                {
                    _logger.LogInformation($"[BinanceService] ✅ System clock is synchronized (difference: {timeDifference:F2}ms)");
                    var operationResult = OperationResult.Success;
                    operationResult.Data = result;
                    operationResult.Message = "System clock is synchronized with Binance server";
                    return operationResult;
                }
                else
                {
                    _logger.LogWarning($"[BinanceService] ⚠️ System clock may be out of sync (difference: {timeDifference:F2}ms)");
                    _logger.LogWarning("[BinanceService] This may cause signature validation errors");
                    _logger.LogWarning("[BinanceService] Please synchronize your system clock with NTP servers");
                    
                    var operationResult = OperationResult.Failed(null, $"System clock out of sync by {timeDifference:F2}ms");
                    operationResult.Data = result;
                    return operationResult;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BinanceService] Exception while checking system clock synchronization");
                return OperationResult.Failed(ex, "Failed to check system clock synchronization");
            }
        }

        /// <summary>
        /// Test account access with detailed error logging for signature issues
        /// </summary>
        public async Task<OperationResult> TestAccountAccessAsync()
        {
            try
            {
                _logger.LogInformation("[BinanceService] Testing account access...");
                
                // First check clock synchronization
                var clockCheck = await CheckSystemClockSynchronizationAsync();
                if (!clockCheck.Succeeded)
                {
                    _logger.LogWarning("[BinanceService] Clock synchronization issue detected before account access test");
                }
                
                // Test account access
                var accountResult = await _restClient.SpotApi.Account.GetAccountInfoAsync();
                
                if (accountResult.Success)
                {
                    _logger.LogInformation("[BinanceService] ✅ Account access successful");
                    var result = OperationResult.Success;
                    result.Message = "Account access successful";
                    result.Data = new
                    {
                        AccountType = accountResult.Data.AccountType,
                        CanTrade = accountResult.Data.CanTrade,
                        CanWithdraw = accountResult.Data.CanWithdraw,
                        CanDeposit = accountResult.Data.CanDeposit,
                        UpdateTime = accountResult.Data.UpdateTime,
                        BalanceCount = accountResult.Data.Balances?.Count() ?? 0,
                        ClockSyncCheck = clockCheck.Data
                    };
                    return result;
                }
                else
                {
                    _logger.LogError($"[BinanceService] ❌ Account access failed: {accountResult.Error?.Message}");
                    _logger.LogError($"[BinanceService] Error Code: {accountResult.Error?.Code}");
                    
                    // Check for specific signature errors
                    if (accountResult.Error?.Message?.Contains("Signature for this request is not valid") == true)
                    {
                        _logger.LogError("[BinanceService] 🔑 SIGNATURE ERROR DETECTED!");
                        _logger.LogError("[BinanceService] Possible causes:");
                        _logger.LogError("[BinanceService] 1. Invalid API Key or Secret Key");
                        _logger.LogError("[BinanceService] 2. System clock out of sync (check time synchronization)");
                        _logger.LogError("[BinanceService] 3. API Key doesn't have required permissions");
                        _logger.LogError("[BinanceService] 4. IP address not whitelisted");
                        _logger.LogError("[BinanceService] 5. Using wrong environment (testnet vs mainnet)");
                    }
                    
                    var result = OperationResult.Failed(null, $"Account access failed: {accountResult.Error?.Message}");
                    result.Data = new
                    {
                        ErrorCode = accountResult.Error?.Code,
                        ErrorMessage = accountResult.Error?.Message,
                        ClockSyncCheck = clockCheck.Data
                    };
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BinanceService] Exception during account access test");
                return OperationResult.Failed(ex, "Account access test failed with exception");
            }
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

        public async Task<OperationResult> PlaceSpotLimitOrderAsync(int num, string symbol, decimal price, decimal quantity, bool isBuy)
        {
            var side = isBuy ? OrderSide.Buy : OrderSide.Sell;
            var sideStr = side.ToString().ToUpperInvariant();
            //_logger.LogDebug($"[BinanceService] Place order {num}: {side.ToString()} {quantity} {symbol} at {price}.");
            Log.ForContext("LogId", $"{symbol}").Information($"[BinanceService] Place order {num}: {side.ToString()} {quantity} {symbol} at {price}");
            var orderResult = await _restClient.SpotApi.Trading.PlaceOrderAsync(
                symbol, 
                side: isBuy? OrderSide.Buy: OrderSide.Sell, 
                type: SpotOrderType.Limit, 
                quantity: quantity,
                price: price,
                timeInForce: TimeInForce.GoodTillCanceled
                );
            if (orderResult.Success)
            {
                OperationResult result = OperationResult.Success;
                result.Data = orderResult.Data.Id;
                return result;
            }
            else
            {
                _logger.LogDebug($"Order failed: {orderResult.Error}");
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
                _logger.LogDebug($"Error fetching trading fee");
                return 0;
            }
        }

        public async Task<OperationResult> GetAccoutInfoAsync()
        {
            var accountInfoResult = await _restClient.SpotApi.Account.GetAccountInfoAsync();
            if (accountInfoResult.Success)
            {
                var accountInfo = accountInfoResult.Data;
                OperationResult result = OperationResult.Success;
                result.Data = accountInfo;
                return result;
            }
            else
            {
                return OperationResult.Failed(null, "Failed to fetch account info");
            }
        }

        public async Task ConnectSocketTradingAsync(string symbol, int num)
        {
            _logger.LogDebug($"[BinanceService]: Subscribing to {symbol} trade updates...");
            var subscriptionResult = await _socketClient.SpotApi.ExchangeData.SubscribeToMiniTickerUpdatesAsync(
                symbol,
                data =>
                {
                    _logger.LogDebug($"{DateTime.Now} Current price {num}: {data.Data.LastPrice}");
                });
            var dataSubscription = subscriptionResult.Data;
            dataSubscription.ConnectionLost += () => _logger.LogDebug("Connection lost");
            dataSubscription.ConnectionRestored += (x) => _logger.LogDebug("Connection restored");
            var subId = subscriptionResult.Data.Id;
            var socketId = subscriptionResult.Data.SocketId;
            var connections = _socketClient.SpotApi.CurrentConnections;
            _logger.LogDebug($"[BinanceService]: Subscribed with ID: {subId} - socket {socketId} - amount connections {connections}");
            if (!subscriptionResult.Success)
            {
                _logger.LogDebug($"Failed to subscribe: {subscriptionResult.Error}");
            }
        }

        public async Task CancelAllOrderAsync(string symbol)
        {
            var rs = await _restClient.SpotApi.Trading.CancelAllOrdersAsync(symbol).ConfigureAwait(false);
            _logger.LogDebug($"[BinanceService] Cancel all orders for {symbol} - Success: {rs.Success}");
        }

        /// <summary>
        /// Subscribe to a symbol, returning a GUID you can use to unsubscribe.
        /// </summary>
        public async Task<int> SubscribeMiniTickerAsync(
            string symbol,
            Func<double, string, Task> onData,
            string orderBookId,
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
                        async data => {
                            //_logger.LogDebug($"[BinanceService-{orderBookId}] Current price: {data.Data.LastPrice}");
                            await onData((double)data.Data.LastPrice, orderBookId);
                        }
                     )
                    .ConfigureAwait(false);

                if (!res.Success)
                {
                    _logger.LogError($"[BinanceService-{orderBookId}] Failed to subscribe to mini ticker updates: {res.Error}");
                    throw new Exception(res.Error?.Message ?? "Subscription failed");
                }
                return  res.Data.Id;
            }
            finally
            {
                // release after 350 ms
                _ = Task.Delay(350, ct).ContinueWith(_ => _throttle.Release());
            }
        }

        public async Task TrackingTickerAsync(string symbol)
        {
            try
            {
                var res = await _socketClient.SpotApi.ExchangeData.SubscribeToTickerUpdatesAsync(
                                    symbol,
                                    data =>
                                    {
                                        _logger.LogDebug($"[BinanceService] Ticker update: {data.Data.Symbol} - Last Price: {data.Data.LastPrice}");
                                    });
                if (!res.Success)
                {
                    _logger.LogDebug($"Failed to subscribe to ticker updates: {res.Error}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Error subscribing to ticker updates: {ex.Message}");
            }
        }

        public async Task<OperationResult> SubscribeUserDataAsync(string symbol, Func<long, bool, Task> onData, string orderBookId, CancellationToken ct = default)
        {
            if (_userDataSubscriptionId.HasValue)
            {
                _logger.LogDebug($"Already subscribed to user data updates.");
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
                _listenKey = listenKeyResult.Data.ToString() ?? "";
                bool isFirst = true;
                var res = await _socketClient.SpotApi.Account.SubscribeToUserDataUpdatesAsync(
                    _listenKey,
                    onOrderUpdateMessage: async data =>
                    {
                        var order = data.Data;
                        if (order.Status == OrderStatus.Filled && order.QuantityFilled == order.Quantity)
                        {
                            if (isFirst)
                            {
                                isFirst = false;
                                await Task.Delay(10000, ct); // Delay 1 second on first call
                            }
                            _logger.LogDebug($"[BinanceService] Order FULL FILLED! Symbol: {order.Symbol}, OrderId: {order.Id}, Price: {order.Price}, Quantity: {order.Quantity}, Side: {order.Side}");
                            await onData(order.Id, true); // Invoke the callback with the order ID
                        }
                        else if (order.Status == OrderStatus.PartiallyFilled)
                        {
                            if (isFirst)
                            {
                                isFirst = false;
                                await Task.Delay(10000, ct);
                            }
                            _logger.LogDebug($"[BinanceService] Order PARTIAL FILLED! Symbol: {order.Symbol}, OrderId: {order.Id}, Price: {order.Price}, Quantity: {order.Quantity}/{order.QuantityFilled}, Side: {order.Side}");
                            await onData(order.Id, false); // Invoke the callback with the order ID
                        }
                    }
                );
                if (!res.Success)
                    throw new Exception(res.Error?.Message ?? "Subscription failed");
                else
                {
                    var dataSubscription = res.Data;
                    dataSubscription.ConnectionLost += () => _logger.LogDebug("Connection lost");
                    dataSubscription.ConnectionRestored += (x) => _logger.LogDebug("Connection restored");
                    _userDataSubscriptionId = res.Data.Id;
                    _logger.LogDebug("Subscribed to user data updates.");
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
                                _logger.LogDebug("Failed to keep user stream alive: " + keepAliveResult.Error);
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
                _logger.LogDebug("Unsubscribed from user data updates.");
            }
            else
            {
                _logger.LogDebug("No active user data subscription to unsubscribe.");
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
        //                _logger.LogDebug("Failed to keep user stream alive: " + keepAliveResult.Error);
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
            var tickDecimal = NumberHandler.GetPrecisionFromMantissa(tickSize);
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
            var stepDecimal = NumberHandler.GetPrecisionFromMantissa(stepSize);
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
