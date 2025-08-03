using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using BotTradingCrypto.Domain.Utilities.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Application
{
    public class SpotGridTradingService : ISpotGridTradingService
    {
        private readonly IBinanceService _binanceService;
        private readonly IMemoryCache _cache;
        private readonly IOrderBookStore _orderBookStore;
        private readonly ILogger<SpotGridTradingService> _logger;
        private decimal _tradingQuantity;
        private List<int> subIds = new List<int>();
        public SpotGridTradingService(
            IBinanceService binanceService,
            IMemoryCache cache,
            IOrderBookStore orderBookStore,
            ILogger<SpotGridTradingService> logger
           )
        {
            _binanceService = binanceService;
            _cache = cache;
            _orderBookStore = orderBookStore;
            _logger = logger;
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
            await ConnectUserSocket(orderBook);
            await InitGridTrading(orderBook);
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
            // Correct the delegate type to match the async method signature.  
            Action<double, string> executeGrid = async (currentPrice, id) =>
           {
               try
               {
                   await ExecuteGridTradesAsync(currentPrice, id);
               }
               catch (Exception ex)
               {
                   _logger.LogError(ex, "Error executing grid trades.");
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
            Action<long> handleFilledOrder = async (id) =>
            {
                try
                {
                    await HandleFilledOrder(id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling filled order.");
                }
            };
            await _binanceService.SubscribeUserDataAsync(orderBook.Symbol, handleFilledOrder, orderBook.Id);
        }

        public async Task InitGridTrading(OrderBook orderBook)
        {
            Task.Delay(2000).Wait();
            Console.WriteLine("----------Init grid trading----------------");
            var stepSize = await _binanceService.GetStepSize(orderBook.Symbol);
            var stickSize = await _binanceService.GetTickSize(orderBook.Symbol);
            orderBook.StepSize = stepSize;
            orderBook.StickSize = stickSize;

            var bookDetail = orderBook.OrderBookDetail;
            var totalGrid = bookDetail.TotalGrid;

            var bid = await _binanceService.GetCurrentPriceAsync(orderBook.Symbol);
            var ask = bid * (1 + bookDetail.InitialGapPercent);

            //Place order at grid 0
            var id = await PlaceSpotLimitBuyOrderAsync(bid, 0, orderBook);
            var order_0 = new GridOrder()
            {
                Id = id,
                Ask = Math.Round(ask, stickSize),
                Bid = Math.Round(bid, stickSize),
                GapPercent = bookDetail.InitialGapPercent,
                GridLevel = 0,
                Quantity = bookDetail.BaseQuantity,
                Side = OrderType.Buy,
                Status = OrderStatus.New,
                CreatedAt = DateTime.UtcNow
            };
            orderBook.GridOrders.Add(order_0);

            for (int i = 1; i < totalGrid; i++)
            {
                ask = bid; // For sell orders, ask is usually the same as prev bid.
                var gap = CalculateGapAsync(bookDetail, i);
                bid = await CalculatePriceAsync(bid, gap);
                var quantity = bookDetail.BaseQuantity + (i * bookDetail.QuantityIncrement);
                quantity = Math.Round(quantity, stepSize);
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
            Console.WriteLine("----------Save done----------------");
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
                Console.WriteLine("----------------------------RESET--------------------------------------");
                _logger.LogInformation($"Resetting grid trading for book ID: {bookId} at current price: {currPrice}");

                // update the order book with the reset increment percent
                var bookDetail = orderBook.OrderBookDetail;
                bookDetail.InitialGapPercent = bookDetail.InitialGapPercent * (1 + bookDetail.ResetIncrementPercent);

                var bid = currPrice;
                var ask = bid * (1 + bookDetail.InitialGapPercent);
                var id = await PlaceSpotLimitBuyOrderAsync(bid, 0, orderBook);
                var order_0 = orderBook.GridOrders.FirstOrDefault(x => x.GridLevel == 0);
                if (order_0 != null)
                {
                    order_0.Id = id;
                    order_0.Ask = ask;
                    order_0.Bid = bid;
                    order_0.GapPercent = bookDetail.InitialGapPercent;
                    order_0.Side = OrderType.Buy;
                    order_0.Status = OrderStatus.New;
                    order_0.CreatedAt = DateTime.UtcNow;
                }
                else
                {
                    order_0 = new GridOrder()
                    {
                        Id = id,
                        Ask = ask,
                        Bid = bid,
                        GapPercent = bookDetail.InitialGapPercent,
                        GridLevel = 0,
                        Quantity = bookDetail.BaseQuantity,
                        Side = OrderType.Buy,
                        Status = OrderStatus.New,
                        CreatedAt = DateTime.UtcNow
                    };
                    orderBook.GridOrders.Add(order_0);
                }
                for (int i = 1; i < bookDetail.TotalGrid; i++)
                {
                    var order_i = orderBook.GridOrders.FirstOrDefault(x => x.GridLevel == i);
                    ask = bid; // For sell orders, ask is usually the same as prev bid.
                    var gap = CalculateGapAsync(bookDetail, i);
                    bid = await CalculatePriceAsync(bid, gap);
                    id = await PlaceSpotLimitBuyOrderAsync(bid, i, orderBook, order_i?.Quantity ?? 0);
                    if (order_i != null)
                    {
                        order_i.Id = id;
                        order_i.Ask = ask;
                        order_i.Bid = bid;
                        order_i.GapPercent = gap;
                        order_i.Side = OrderType.Buy;
                        order_i.Status = OrderStatus.New;
                        order_i.CreatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        order_i = new GridOrder()
                        {
                            Id = id,
                            Ask = ask,
                            Bid = bid,
                            GapPercent = bookDetail.InitialGapPercent,
                            GridLevel = i,
                            Quantity = bookDetail.BaseQuantity,
                            Side = OrderType.Buy,
                            Status = OrderStatus.New,
                            CreatedAt = DateTime.UtcNow
                        };
                        orderBook.GridOrders.Add(order_i);
                    }
                }
                var rs = await _orderBookStore.UpdateOrderBook(orderBook); // Update the order book in the store
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
                Console.WriteLine($"{DateTime.Now}:Executing grid trades at price: {price} for book ID: {bookId}");

                // Retrieve the order book for the given book ID.
                var orderBook = await _cache.GetOrCreateAsync(bookId, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30); // Cache for 30 minutes
                    return await _orderBookStore.GetOrderBookAsync(bookId);
                });
                if (orderBook == null)
                {
                    _logger.LogWarning("Order book with ID {BookId} not found.", bookId);
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
                    var gap = CalculateGapAsync(orderBook!.OrderBookDetail, (lowestGridOrder?.GridLevel ?? 0) + 1);
                    var priceGrid = await CalculatePriceAsync(lowestGridPrice, gap);
                    if (price <= priceGrid)
                    {
                        await PlaceSpotLimitBuyOrderAsync(priceGrid, (lowestGridOrder?.GridLevel ?? 0) + 1, orderBook);
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"{DateTime.Now}:Error executing grid trades: {ex.Message}");
            }
            
        }
        public async Task HandleFilledOrder(long id)
        {
            try
            {
                Task.Delay(5000).Wait(); // Simulate a delay for processing
                Console.WriteLine("----------Begin filled----------------");
                //check order id in list -> handle order 
                var book = await _orderBookStore.GetOrderBookByOrderIdAsync(id);
                var order = book.GridOrders.Where(o => o.Id == id).FirstOrDefault();
                if (order == null)
                {
                    Console.WriteLine($"----Not found order-{id}");
                    return;
                }
                // Buy -> Sell
                if (order.Side == OrderType.Buy)
                {
                    Console.WriteLine("----------Place sell order----------------");
                    //var priceSell = await CalculatePriceAsync(order.GridLevel, order.Bid);
                    id = await PlaceSpotLimitSellOrderAsync(order.Ask, order.GridLevel, book, order.Quantity);
                    if (id <= 0)
                    {
                        _logger.LogError("Failed to place sell order for grid {GridLevel} at price {Price}.", order.GridLevel, order.Ask);
                        return; // Exit if the sell order placement failed.
                    }
                    else
                    {
                        order.Id = id;
                        order.Side = OrderType.Sell;
                        order.Status = OrderStatus.New;
                    }
                }
                //Sell -> Buy and calculate profit
                else
                {
                    var fee = await _binanceService.GetTradingFeeAsynce(book.Symbol);
                    Console.WriteLine($" -----------Fee of order: {fee} ------------");
                    var amount = order.Ask * order.Quantity - fee;
                    var profit = (amount - (order.Bid * order.Quantity)) / order.Bid * order.Quantity;
                    var buyQuantity = Math.Round(amount / order.Bid, book.StepSize);
                    var growthRate = book.OrderBookDetail.CompoundGrowthRate;
                    //if (growthRate > 0 && profit > growthRate)
                    //{
                    //    buyQuantity += (buyQuantity * growthRate);
                    //}
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
                    }
                    Console.WriteLine("----------Begin Update----------------");
                    id = await PlaceSpotLimitBuyOrderAsync(order.Bid, order.GridLevel, book, buyQuantity);
                    Console.WriteLine("----------Done filled----------------");
                    order.Id = id;
                }

                var rs = await _orderBookStore.UpdateOrderBook(book); // Update the order book in the store
                if (rs.Succeeded)
                {
                    _cache.Set(book.Id, book, TimeSpan.FromMinutes(30)); // Update the cache with the new order book
                }
                Console.WriteLine($"{DateTime.Now}:Handling filled order with ID: {id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling filled order with ID: {OrderId}", id);
                return;
            }
        }
        public async Task<long> PlaceSpotLimitBuyOrderAsync(double price, int gridNumber, OrderBook orderBook, double quantity = 0)
        {
            
            price = Math.Round(price, orderBook.StickSize);
            if (quantity == 0)
            {
                quantity = orderBook.OrderBookDetail.BaseQuantity + (gridNumber * orderBook.OrderBookDetail.QuantityIncrement);
            }
            quantity = Math.Round(quantity, orderBook.StepSize);
            var rs = await _binanceService.PlaceSpotLimitOrderAsync(orderBook.Symbol, (decimal)price, (decimal)quantity, true);
            if (!rs.Succeeded)
            {
                _logger.LogError("Failed to place buy order for grid {GridNumber} at price {Price}. Error: {ErrorMessage}", gridNumber, price, rs.Message);
                return 0;
            }


            return (long)(rs.Data??"0");
        }

        public async Task<long> PlaceSpotLimitSellOrderAsync(double price, int gridNumber, OrderBook orderBook, double quantity = 0)
        {
            try
            {
                //var rs = await _binanceService.GetAccoutInfoAsync();
                //var balance = 0;
                //if (rs.Succeeded)
                //{
                //    //var data = (BinanceAccountInfo)rs.Data. ?? 0;
                //}
                if (quantity == 0)
                {
                    quantity = orderBook.OrderBookDetail.BaseQuantity + (gridNumber * orderBook.OrderBookDetail.QuantityIncrement);
                }
                price = Math.Round(price, orderBook.StickSize);
                quantity = Math.Round(quantity, orderBook.StepSize);
                var rs = await _binanceService.PlaceSpotLimitOrderAsync(orderBook.Symbol, (decimal)price, (decimal)quantity, false);
                if (!rs.Succeeded)
                {
                    _logger.LogError("Failed to place buy order for grid {GridNumber} at price {Price}. Error: {ErrorMessage}", gridNumber, price, rs.Message);
                }
                return (long)(rs.Data ?? "0");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error placing sell order for grid {GridNumber} at price {Price}", gridNumber, price);
                return 0; // Return 0 or an appropriate value to indicate failure.
            }
        }
        public double CalculateGapAsync(OrderBookDetail orderBookDetail, int gridNumber)
        {
            // Calculate the gap based on the current price and grid configuration.
            var gap = orderBookDetail.InitialGapPercent;
            gap = Math.Min(gap, orderBookDetail.MaxGapPercent);
            var reductionPercent = orderBookDetail.GapReductionPercent;
            reductionPercent = Math.Max(Math.Min(reductionPercent, 0.99), 0.5); // Ensure reduction percent is at least 1%
            //if (type == OrderType.Buy)
            //{
            //    gap = gap * Math.Pow(reductionPercent, gridNumber);                
            //}
            //else
            //{
            //    gap = gap * Math.Pow(reductionPercent, gridNumber - 1);
            //}
            gap = gap * Math.Pow(reductionPercent, gridNumber);
            gap = Math.Round(gap, 5);
            gap = Math.Max(gap, orderBookDetail.MinGapPercent);
            return gap;
        }
        public async Task<double> CalculatePriceAsync(double price, double gap)
        {           
            var placedPrice = price;
            placedPrice = price * (1 - gap);
            return placedPrice;
        }
    }
}
