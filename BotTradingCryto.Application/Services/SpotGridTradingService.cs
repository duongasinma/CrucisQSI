using BotTradingCrypto.Domain;
using BotTradingCrypto.Domain.Utilities.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace BotTradingCrypto.Application
{
    public class SpotGridTradingService : ISpotGridTradingService
    {
        private readonly IBinanceService _binanceService;
        private readonly IMemoryCache _cache;
        private readonly IOrderBookStore _orderBookStore;
        private readonly ILogger<SpotGridTradingService> _logger;
        private List<int> subIds = new List<int>();
        private readonly LockProvider _lockProvider;
        public SpotGridTradingService(
            IBinanceService binanceService,
            IMemoryCache cache,
            IOrderBookStore orderBookStore,
            ILogger<SpotGridTradingService> logger,
            LockProvider lockProvider
           )
        {
            _binanceService = binanceService;
            _cache = cache;
            _orderBookStore = orderBookStore;
            _logger = logger;
            _lockProvider = lockProvider;
        }
        public async Task StartGridTradingAsync(string symbol, OrderBookDetail orderBookDetail)
        {
            if (_cache is MemoryCache concreteCache)
            {
                concreteCache.Clear(); // Clears all cache entries
            }

            var orderBook = new OrderBook()
            {
                Symbol = symbol,
                OrderBookDetail = orderBookDetail
            };
            // Initialize grid trading and subscribe to the mini ticker for the symbol.
            //await ConnectUserSocket(orderBook);
            //await InitGridTrading(orderBook);

            Task handOrder = ConnectUserSocket(orderBook);
            Task initGrid =  InitGridTrading(orderBook);
            await Task.WhenAll(handOrder, initGrid);
            // Connect to the WebSocket for real-time updates.
            await ConnectWebSocket(orderBook);
        }
        public async Task<bool> StopGridTradingAsync(int subId)
        {
            if (subIds.Contains(subId))
            {
                subIds.Remove(subId);
                await _binanceService.UnsubscribeMiniTickerAsync(subId);
                return true;
            }
            else
            {
                _logger.LogWarning("Subscription ID {SubId} not found in active subscriptions.", subId);
                return false;
            }
        }
        public async Task ConnectWebSocket(OrderBook orderBook)
        {
            _logger.LogDebug($"[SpotGridTradingService] ---------- ConnectWebSocket -------------");
            // Correct the delegate type to match the async method signature.  
            var handlerLock = _lockProvider.GetLock($"{orderBook.Id}");
            Func<double, string, Task> executeGrid =  async(price, id) =>
            {
                await handlerLock.WaitAsync();
                try
                {
                    // Your grid execution logic here
                    await ExecuteGridTradesAsync(price, id);
                }
                finally
                {
                    handlerLock.Release();
                }
            };  

            var symbolStreamId = await _binanceService.SubscribeMiniTickerAsync(orderBook.Symbol, executeGrid, orderBook.Id);

            //Update the order book with the subscription ID.
            orderBook.SubscriptionId = symbolStreamId.ToString();
            var rs = await _orderBookStore.UpdateOrderBook(orderBook); // Ensure the order book is updated with the subscription ID.
            if (rs.Succeeded)
            {
                _cache.Set(orderBook.Id, orderBook, TimeSpan.FromMinutes(30)); // Update the cache with the new order book
            }
        }
        public async Task ConnectUserSocket(OrderBook orderBook)
        {
            _logger.LogDebug($"[SpotGridTradingService] ---------- ConnectUserSocket ------------");
            var handlerLock = _lockProvider.GetLock($"{orderBook.Id}");
            Func<long, bool, Task> handleFilledOrder = async (id, fullFilled) =>
            {
                await handlerLock.WaitAsync();
                try
                {
                    // Your order handling logic here
                    await HandleFilledOrder(id, fullFilled);
                }
                finally
                {
                    handlerLock.Release();
                }
            };
            await _binanceService.SubscribeUserDataAsync(orderBook.Symbol, handleFilledOrder, orderBook.Id);
        }

        public async Task InitGridTrading(OrderBook orderBook)
        {
            await Task.Delay(1000);
            _logger.LogDebug($"[SpotGridTradingService] ---------- InitGridTrading ----------------");
            var stepSize = await _binanceService.GetStepSize(orderBook.Symbol);
            var stickSize = await _binanceService.GetTickSize(orderBook.Symbol);
            
            orderBook.StepSize = stepSize;
            orderBook.StickSize = stickSize;

            var bookDetail = orderBook.OrderBookDetail;
            var totalGrid = bookDetail.TotalGrid;

            var bid = await _binanceService.GetCurrentPriceAsync(orderBook.Symbol);
            var ask = bid * (1 + bookDetail.InitialGapPercent);

            var tradeValue = bookDetail.BaseValue;
            //Place order at grid 0
            var id = await PlaceSpotLimitBuyOrderAsync(bid, 0, orderBook);
            var order_0 = new GridOrder()
            {
                Id = id,
                Ask = Math.Round(ask, stickSize),
                Bid = Math.Round(bid, stickSize),
                GapPercent = bookDetail.InitialGapPercent,
                GridLevel = 0,
                Quantity = CalculateQuantity(bid, tradeValue, stepSize),
                Side = OrderType.Buy,
                Status = OrderStatus.New,
                CreatedAt = DateTime.UtcNow
            };
            orderBook.GridOrders.Add(order_0);

            for (int i = 1; i < totalGrid; i++)
            {
                ask = bid; // For sell orders, ask is usually the same as prev bid.
                var gap = CalculateGapAsync(bookDetail, i);
                bid = CalculatePrice(bid, gap, stickSize);
                tradeValue = bookDetail.BaseValue + (i * bookDetail.ValueIncrement);
                var quantity = CalculateQuantity(bid, tradeValue, stepSize);

                id = await PlaceSpotLimitBuyOrderAsync(bid, i, orderBook, quantity);
                if (id <= 0)
                {
                    _logger.LogError("Failed to place order for grid level {GridLevel} at price {Price}.", i, bid);
                    continue; // Skip this iteration if the order placement failed.
                }
                var order_i = new GridOrder()
                {
                    Id = id,
                    Ask = Math.Round(ask, stickSize),
                    Bid = Math.Round(bid, stickSize),
                    GapPercent = gap,
                    GridLevel = i,
                    Quantity = quantity,
                    Side = OrderType.Buy,
                    Status = OrderStatus.New,
                    CreatedAt = DateTime.UtcNow
                };
                orderBook.GridOrders.Add(order_i);
            }
            var rs = await _orderBookStore.InsertOrderBook(orderBook); // Save the order book to the store.
            _logger.LogDebug($"[SpotGridTradingService] ----------Save MONGODB----------------");
            if (!rs.Succeeded)
            {
                _logger.LogError("Failed to initialize grid trading for symbol {Symbol}. Error: {ErrorMessage}", orderBook.Symbol, rs.Message);
                return;
            }
            _cache.Set(orderBook.Id, orderBook, TimeSpan.FromMinutes(30)); // Cache the order book for 30 minutes.
        }

        public async Task ResetGridTradingAsync(string bookId, double currPrice, OrderBook orderBook)
        {
            try
            {
                //*Check case where some sell orders are not filled and cancel all orders.
                await _binanceService.CancelAllOrderAsync(orderBook.Symbol);
                _logger.LogDebug($"[SpotGridTradingService]----------RESET----------");
                _logger.LogInformation($"Resetting grid trading for book ID: {bookId} at current price: {currPrice}");

                // update the order book with the reset increment percent
                var bookDetail = orderBook.OrderBookDetail;
                bookDetail.InitialGapPercent = bookDetail.InitialGapPercent * (1 + bookDetail.ResetIncrementPercent);
            
                var gap = bookDetail.InitialGapPercent;
                var bid = currPrice;
                var ask = bid * (1 + gap);
                for (int i = 0; i < bookDetail.TotalGrid; i++)
                {
                    if(i != 0)
                    {
                        ask = bid; // For sell orders, ask is usually the same as prev bid.
                        gap = CalculateGapAsync(bookDetail, i);
                        bid = CalculatePrice(bid, gap, orderBook.StickSize);
                    }
                    var order_i = orderBook.GridOrders.FirstOrDefault(x => x.GridLevel == i);
                    if (order_i != null)
                    {
                        var tradeValue = order_i.Bid * order_i.Quantity; // Calculate the trade value based on the existing order's bid and quantity.
                        var quantity = CalculateQuantity(bid, tradeValue, orderBook.StepSize);
                        var id = await PlaceSpotLimitBuyOrderAsync(bid, 0, orderBook, quantity);

                        order_i.Id = id;
                        order_i.Ask = ask;
                        order_i.Bid = bid;
                        order_i.GapPercent = gap;
                        order_i.Quantity = quantity;
                        order_i.Side = OrderType.Buy;
                        order_i.Status = OrderStatus.New;
                        order_i.CreatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var tradeValue = bookDetail.BaseValue + (i * bookDetail.ValueIncrement);
                        var quantity = CalculateQuantity(bid, tradeValue, orderBook.StepSize);
                        var id = await PlaceSpotLimitBuyOrderAsync(bid, 0, orderBook);
                        order_i = new GridOrder()
                        {
                            Id = id,
                            Ask = ask,
                            Bid = bid,
                            GapPercent = gap,
                            GridLevel = i,
                            Quantity = quantity,
                            Side = OrderType.Buy,
                            Status = OrderStatus.New,
                            CreatedAt = DateTime.UtcNow
                        };
                        orderBook.GridOrders.Add(order_i);
                    }
                }
                var rs = await _orderBookStore.UpdateOrderBook(orderBook); // Update the order book in the store
                _logger.LogDebug($"[SpotGridTradingService] ----------Save MONGODB----------------");
                _logger.LogDebug("");
                if (rs.Succeeded)
                {
                    _cache.Set(orderBook.Id, orderBook, TimeSpan.FromMinutes(30)); // Update the cache with the new order book
                    _logger.LogInformation("Grid trading reset successfully for book ID: {BookId}", bookId);
                }
                else
                {
                    _logger.LogError("Failed to reset grid trading for book ID: {BookId}. Error: {ErrorMessage}", bookId, rs.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting grid trading.");
            }
        }
        public async Task ExecuteGridTradesAsync(double price, string bookId)
        {
            try
            {
                // Retrieve the order book for the given book ID.
                var orderBook = await _cache.GetOrCreateAsync(bookId, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30); // Cache for 30 minutes
                    return await _orderBookStore.GetOrderBookAsync(bookId);
                });
                if (orderBook == null)
                {
                    _logger.LogWarning("[SpotGridTradingService] Order book with ID {BookId} not found.", bookId);
                    return;
                }
                var priceGrid_0 = orderBook!.GridOrders.FirstOrDefault(x => x.GridLevel == 0)?.Bid ?? 0;
                var lowestGridOrder = orderBook.GridOrders.OrderByDescending(x => x.GridLevel).FirstOrDefault();
                var lowestGridPrice = lowestGridOrder?.Bid ?? 0;
                var resetPercent = orderBook?.OrderBookDetail.ResetGridPercent;
                //Check price is overhead -> reset grid trading
                if (price >= (priceGrid_0 * (1 + resetPercent)))
                {
                    await ResetGridTradingAsync(bookId, price, orderBook!);
                }
                //Create new order if price is below the last buy order price
                else if (price < lowestGridPrice)
                {
                    var gridLevel = (lowestGridOrder?.GridLevel ?? 0) + 1;
                    var gap = CalculateGapAsync(orderBook!.OrderBookDetail, gridLevel);
                    var priceGrid = CalculatePrice(lowestGridPrice, gap, orderBook.StickSize);
                    if (price <= priceGrid)
                    {
                        _logger.LogDebug($"{DateTime.Now} [SpotGridTradingService] Placing new order at lowest price {priceGrid}, gird - {gridLevel} for book ID: {bookId}");
                        var tradeValue = orderBook.OrderBookDetail.BaseValue + (gridLevel * orderBook.OrderBookDetail.ValueIncrement);
                        var quantity = CalculateQuantity(priceGrid, tradeValue, orderBook.StepSize);
                        var id = await PlaceSpotLimitBuyOrderAsync(priceGrid, gridLevel, orderBook, quantity);
                        if(id <= 0)
                        {
                            _logger.LogError("[SpotGridTradingService] Failed to place order for grid level {GridLevel} at price {Price}.", gridLevel, priceGrid);
                            return; // Exit if the order placement failed.
                        }
                        var order = new GridOrder()
                        {
                            Id = id,
                            Ask = lowestGridPrice,
                            Bid = priceGrid,
                            GapPercent = gap,
                            GridLevel = gridLevel,
                            Quantity = quantity,
                            Side = OrderType.Buy,
                            Status = OrderStatus.New,
                            CreatedAt = DateTime.UtcNow
                        };
                        orderBook.GridOrders.Add(order);
                        // Sell lowest grid order if it exists
                        if (lowestGridOrder != null && lowestGridOrder.Side == OrderType.Buy)
                        {
                            // Place a sell order for the lowest grid order
                            var sellId = await PlaceSpotLimitSellOrderAsync(lowestGridOrder.Ask, lowestGridOrder.GridLevel, orderBook, lowestGridOrder.Quantity);
                            if (sellId > 0)
                            {
                                lowestGridOrder.Id = sellId;
                                lowestGridOrder.Side = OrderType.Sell;
                                lowestGridOrder.Status = OrderStatus.New;
                            }
                            else
                            {
                                _logger.LogError("Failed to place sell order for grid level {GridLevel} at price {Price}.", lowestGridOrder.GridLevel, lowestGridOrder.Ask);
                            }
                        }
                        var rs = await _orderBookStore.UpdateOrderBook(orderBook);
                        _logger.LogDebug($"[SpotGridTradingService] ----------Save MONGODB----------------");
                        if (rs.Succeeded)
                        {
                            _cache.Set(orderBook.Id, orderBook, TimeSpan.FromMinutes(30)); // Update the cache with the new order book
                            _logger.LogDebug($"New grid order placed at price {priceGrid} for book ID: {bookId}");
                        }
                        else
                        {
                            _logger.LogError("Failed to update order book after placing new grid order. Error: {ErrorMessage}", rs.Message);
                        }
                    }
                }
            }   catch (Exception ex) {
                _logger.LogDebug($"Error executing grid trades: {ex.Message}");
            }
            
        }
        public async Task HandleFilledOrder(long id, bool fullFilled)
        {
            try
            {
                _logger.LogDebug($"[SpotGridTradingService] == BEGIN FILLED == order: {id}");
                //check order id in list -> handle order 
                var book = await _orderBookStore.GetOrderBookByOrderIdAsync(id);
                var order = book.GridOrders.Where(o => o.Id == id).FirstOrDefault();
                if (order == null)
                {
                    _logger.LogDebug($"[SpotGridTradingService] Not found order-{id}");
                    return;
                }
                var checklast = order.GridLevel >= (book.GridOrders.Count() - 1);
                var feePercent = await _binanceService.GetTradingFeeAsynce(book.Symbol);
                //Check if the order is full filled or partially filled
                if (!fullFilled)
                {
                    order.NumberFilled++;
                }
                else
                {
                    // Buy -> Sell
                    if (order.Side == OrderType.Buy)
                    {
                        if (checklast)
                        {
                            return;
                        }
                        _logger.LogDebug($"[SpotGridTradingService]  Place SELL order");
                        var quantity = order.Quantity - (order.Quantity * feePercent * order.NumberFilled);
                        quantity = Math.Round(quantity, book.StepSize);
                        //var priceSell = await CalculatePriceAsync(order.GridLevel, order.Bid);
                        Log.ForContext("LogId", $"{order.Side.ToString()}_{book.Symbol}_{book.Id}").Information($"[SpotGridTradingService] Grid {order.GridLevel} {order.Side.ToString()} {quantity} at Price {order.Bid}");
                        id = await PlaceSpotLimitSellOrderAsync(order.Ask, order.GridLevel, book, quantity);
                        if (id <= 0)
                        {
                            _logger.LogError("Failed to place sell order for grid {GridLevel} at price {Price}.", order.GridLevel, order.Ask);
                            return; // Exit if the sell order placement failed.
                        }
                        else
                        {
                            order.Id = id;
                            order.Quantity = quantity;
                            order.Side = OrderType.Sell;
                            order.Status = OrderStatus.New;
                            order.NumberFilled = 1; // Reset the number filled for the next buy order
                        }
                    }
                    //Sell -> Buy and calculate profit
                    else
                    {
                        //_logger.LogDebug($" -----------Fee of order: {fee} ------------");
                        var fee = feePercent * order.NumberFilled * order.Ask * order.Quantity;
                        var tradeValue = order.Ask * order.Quantity - fee;
                        var profit = (tradeValue - (order.Bid * order.Quantity)) / (order.Bid * order.Quantity) * 100;
                        profit = Math.Round(profit, 2); // Round profit to 2 decimal places
                        var buyQuantity = Math.Round(tradeValue / order.Bid, book.StepSize);
                        buyQuantity = Math.Round(buyQuantity, book.StepSize);
                        Log.ForContext("LogId", $"{order.Side.ToString()}_{book.Symbol}_{book.Id}").Information($"[SpotGridTradingService] Grid {order.GridLevel} {order.Side.ToString()} {buyQuantity} at Price {order.Ask} / Profit: {profit}%");
                        if (id <= 0)
                        {
                            _logger.LogError("Failed to place sell order for grid {GridLevel} at price {Price}.", order.GridLevel, order.Ask);
                            return; // Exit if the sell order placement failed.
                        }
                        else
                        {
                            order.Quantity = buyQuantity; // Update the quantity for the next buy order
                            order.Side = OrderType.Buy;
                            order.Status = OrderStatus.New;
                            order.NumberFilled = 1; // Reset the number filled for the next buy order
                        }
                        _logger.LogDebug($"[SpotGridTradingService]  Place BUY order");
                        id = await PlaceSpotLimitBuyOrderAsync(order.Bid, order.GridLevel, book, buyQuantity);
                        order.Id = id;
                    }
                }

                var rs = await _orderBookStore.UpdateOrderBook(book); // Update the order book in the store
                _logger.LogDebug($"[SpotGridTradingService] ----------Save MONGODB----------------");
                if (rs.Succeeded)
                {
                    _cache.Set(book.Id, book, TimeSpan.FromMinutes(30)); // Update the cache with the new order book
                }
                _logger.LogDebug($"[SpotGridTradingService] Place new order with ID: {id}");
                _logger.LogDebug("");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling filled order with ID: {OrderId}", id);
                return;
            }
        }
        public async Task<long> PlaceSpotLimitBuyOrderAsync(double price, int gridNumber, OrderBook orderBook, double quantity = 0)
        {
            const int maxRetries = 10;
            const int delayMs = 60000; // 60 seconds between retries

            price = Math.Round(price, orderBook.StickSize);
            if (quantity == 0)
            {
                var tradeValue = orderBook.OrderBookDetail.BaseValue + (gridNumber * orderBook.OrderBookDetail.ValueIncrement);
                quantity = CalculateQuantity(price, tradeValue, orderBook.StepSize);
            }
            quantity = Math.Round(quantity, orderBook.StepSize);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var rs = await _binanceService.PlaceSpotLimitOrderAsync(gridNumber, orderBook.Symbol, (decimal)price, (decimal)quantity, true);
                if (rs.Succeeded)
                {
                    return (long)(rs.Data ?? "0");
                }

                // Check if the error is due to insufficient balance
                if (rs.Message != null && rs.Message.ToLower().Contains("insufficient"))
                {
                    _logger.LogWarning("Insufficient balance to place buy order for grid {GridNumber} at price {Price}. Attempt {Attempt}/{MaxRetries}. Waiting for balance...", gridNumber, price, attempt, maxRetries);

                    // Optionally, check the actual balance here and log it
                    var balanceResult = await _binanceService.GetAccoutInfoAsync();
                    if (balanceResult.Succeeded)
                    {
                        // Log or inspect balanceResult.Data as needed
                    }

                    await Task.Delay(delayMs);
                    continue;
                }
                else
                {
                    _logger.LogError("Failed to place buy order for grid {GridNumber} at price {Price}. Error: {ErrorMessage}", gridNumber, price, rs.Message);
                    return 0;
                }
            }

            _logger.LogError("Failed to place buy order for grid {GridNumber} at price {Price} after {MaxRetries} attempts due to insufficient balance.", gridNumber, price, maxRetries);
            return 0;
        }

        public async Task<long> PlaceSpotLimitSellOrderAsync(double price, int gridNumber, OrderBook orderBook, double quantity = 0)
        {
            const int maxRetries = 10;
            const int delayMs = 5000; // 5 seconds between retries

            if (quantity == 0)
            {
                var tradeValue = orderBook.OrderBookDetail.BaseValue + (gridNumber * orderBook.OrderBookDetail.ValueIncrement);
                quantity = CalculateQuantity(price, tradeValue, orderBook.StepSize);
            }
            price = Math.Round(price, orderBook.StickSize);
            quantity = Math.Round(quantity, orderBook.StepSize);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var rs = await _binanceService.PlaceSpotLimitOrderAsync(gridNumber, orderBook.Symbol, (decimal)price, (decimal)quantity, false);
                if (rs.Succeeded)
                {
                    return (long)(rs.Data ?? "0");
                }

                // Check if the error is due to insufficient balance
                if (rs.Message != null && rs.Message.ToLower().Contains("insufficient"))
                {
                    _logger.LogWarning("Insufficient balance to place sell order for grid {GridNumber} at price {Price}. Attempt {Attempt}/{MaxRetries}. Waiting for balance...", gridNumber, price, attempt, maxRetries);

                    // Optionally, check the actual balance here and log it
                    var balanceResult = await _binanceService.GetAccoutInfoAsync();
                    if (balanceResult.Succeeded)
                    {
                        // Log or inspect balanceResult.Data as needed
                    }

                    await Task.Delay(delayMs);
                    continue;
                }
                else
                {
                    _logger.LogError("Failed to place sell order for grid {GridNumber} at price {Price}. Error: {ErrorMessage}", gridNumber, price, rs.Message);
                    return 0;
                }
            }

            _logger.LogError("Failed to place sell order for grid {GridNumber} at price {Price} after {MaxRetries} attempts due to insufficient balance.", gridNumber, price, maxRetries);
            return 0;
        }
        public double CalculateGapAsync(OrderBookDetail orderBookDetail, int gridNumber)
        {
            // Calculate the gap based on the current price and grid configuration.
            var gap = orderBookDetail.InitialGapPercent;
            gap = Math.Min(gap, orderBookDetail.MaxGapPercent);
            var reductionPercent = orderBookDetail.GapReductionPercent;
            reductionPercent = Math.Max(Math.Min(reductionPercent, 0.99), 0.5); // Ensure reduction percent is at least 1%
         
            gap = gap * Math.Pow(reductionPercent, gridNumber);
            gap = Math.Round(gap, 5);
            gap = Math.Max(gap, orderBookDetail.MinGapPercent);
            return gap;
        }
        public double CalculatePrice(double price, double gap, int stickSize)
        {           
            var placedPrice = price;
            placedPrice = price * (1 - gap);
            return Math.Round(placedPrice, stickSize);
        }
        public double CalculateQuantity(double price, double value, int stepSize)
        {
            return Math.Round(value / price, stepSize);
        }
    }
}
