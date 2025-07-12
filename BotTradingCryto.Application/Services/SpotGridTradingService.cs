using BotTradingCrypto.Application;
using BotTradingCrypto.Domain;
using BotTradingCryto.Application;
using BotTradingCryto.Domain;
using BotTradingCryto.Domain.Utilities.Enums;
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
        private string _symbol;
        private decimal _tradingQuantity;
        private List<int> subIds = new List<int>();
        private readonly IOptionsSnapshot<GridConfiguration> _gridConfiguration;
        private OrderBook _orderBook;
        public SpotGridTradingService(
            IBinanceService binanceService,
            IMemoryCache cache,
            IOrderBookStore orderBookStore,
            ILogger<SpotGridTradingService> logger,
            IOptionsSnapshot<GridConfiguration> gridConfiguration
           )
        {
            _binanceService = binanceService;
            _cache = cache;
            _orderBookStore = orderBookStore;
            _logger = logger;
            _gridConfiguration = gridConfiguration;
        }
        public async Task StartGridTradingAsync(string symbol)
        {
            _symbol = symbol;
            _orderBook = new OrderBook()
            {
                Id = Guid.NewGuid(),
                Symbol = _symbol,
            };
            // Initialize grid trading and subscribe to the mini ticker for the symbol.
            //await InitGridTrading();
            await ConnectWebSocket(_symbol);
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
        public async Task ConnectWebSocket(string symbol)
        {
            // Correct the delegate type to match the async method signature.  
            Action<double, Guid> executeGrid = async (currentPrice, id) =>
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
            var symbolStreamId = await _binanceService.SubscribeMiniTickerAsync(symbol, executeGrid, _orderBook.Id);
            await _binanceService.SubscribeUserDataAsync(symbol, handleFilledOrder);
            _orderBook.SubscriptionId = symbolStreamId.ToString();
        }

        public async Task InitGridTrading()
        {
            var totalGrid = _gridConfiguration.Value.TotalGrid;
            var price = await _binanceService.GetCurrentPriceAsync(_symbol);
            //Place order at grid 0
            await PlaceSpotLimitBuyOrderAsync(price, 0);
            for (int i = 1; i<= totalGrid; i++)
            {
                price = await CalculatePriceAsync(i, price, OrderType.Buy);
                await PlaceSpotLimitBuyOrderAsync(price, i);
            }
        }

        public async Task ResetGridTradingAsync(Guid bookId, double currPrice, OrderBook orderBook)
        {
            try
            {
                var priceGrid_0 = orderBook.gridOrders.FirstOrDefault(x => x.GridLevel == 0)?.Price ?? 0;
                var quantityGrid_0 = orderBook.gridOrders.FirstOrDefault(x => x.GridLevel == 0)?.Quantity ?? 0;
                await PlaceSpotLimitBuyOrderAsync(priceGrid_0, 0,quantityGrid_0);
                for (int i = 1; i <= _gridConfiguration.Value.TotalGrid; i++)
                {
                    double quantityGrid = 0;
                    var priceGrid = await CalculatePriceAsync(i, currPrice, OrderType.Buy);
                    var orderGrid = orderBook.gridOrders.FirstOrDefault(x => x.GridLevel == i);
                    if(orderGrid != null)
                    {
                        quantityGrid = orderGrid.Quantity * (1 + _gridConfiguration.Value.ResetIncrementPercent);
                    }
                    else
                    {
                        quantityGrid = _gridConfiguration.Value.BaseQuantity + (i * _gridConfiguration.Value.QuantityIncrement);
                    }
                    await PlaceSpotLimitBuyOrderAsync(priceGrid, i, quantityGrid);
                }
                await _binanceService.CancelAllOrderAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting grid trading.");
            }
        }
        public async Task ExecuteGridTradesAsync(double price, Guid bookId)
        {
            Console.WriteLine($"{DateTime.Now}:Executing grid trades at price: {price} for book ID: {bookId}");
            var resetPercent = _gridConfiguration.Value.ResetGridPercent;

            // Retrieve the order book for the given book ID.
            var orderBook = await _orderBookStore.GetOrderBookAsync(bookId);
            var priceGrid_0 = orderBook.gridOrders.FirstOrDefault(x => x.GridLevel == 0)?.Price ?? 0;
            var lowestGridOrder = orderBook.gridOrders.OrderByDescending(x => x.GridLevel).FirstOrDefault();
            var lowestGridPrice = lowestGridOrder?.Price ?? 0;

            //Check price is overhead -> reset grid trading
            if (price > priceGrid_0 && price >= (priceGrid_0 * (1 + resetPercent)))
            {
                await ResetGridTradingAsync(bookId, price, orderBook);
            }
            //Create new order if price is below the last buy order price
            else if (price < lowestGridPrice && lowestGridOrder != null)
            {
                var priceGrid = await CalculatePriceAsync(lowestGridOrder.GridLevel + 1, price, OrderType.Buy);
                if(price <= priceGrid)
                {
                    await PlaceSpotLimitBuyOrderAsync(priceGrid, lowestGridOrder.GridLevel + 1);
                }
            }
        }
        public async Task HandleFilledOrder(long id)
        {
            //check order id in list -> handle order 
            //var orderBook = await _orderBookStore.GetOrderBookAsync(id);
            // Buy -> Sell
            //Sell -> Buy and calculate profit
            Console.WriteLine($"{DateTime.Now}:Handling filled order with ID: {id}");
        }
        public async Task PlaceSpotLimitBuyOrderAsync(double price, int gridNumber, double quantity = 0)
        {
            var stepSize = await _binanceService.GetStepSize(_symbol);
            var stickSize = await _binanceService.GetTickSize(_symbol);
            price = Math.Round(price, stickSize);
            if (quantity == 0)
            {
                quantity = _gridConfiguration.Value.BaseQuantity + (gridNumber * _gridConfiguration.Value.QuantityIncrement);
            }
            quantity = Math.Round(quantity, stepSize);
            var rs = await _binanceService.PlaceSpotLimitOrderAsync(_symbol, (decimal)price, (decimal)quantity, true);
            if (!rs.Succeeded)
            {
                _logger.LogError("Failed to place buy order for grid {GridNumber} at price {Price}. Error: {ErrorMessage}", gridNumber, price, rs.Message);
            }
        }

        public async Task PlaceSpotLimitSellOrderAsync(double price, int gridNumber, double quantity = 0)
        {
            var stepSize = await _binanceService.GetStepSize(_symbol);
            var stickSize = await _binanceService.GetTickSize(_symbol);
            price = Math.Round(price, stickSize);
            if (quantity == 0)
            {
                
            }
        }
        public double CalculateGapAsync(int gridNumber, OrderType type)
        {
            // Calculate the gap based on the current price and grid configuration.
            var gridConfig = _gridConfiguration.Value;
            var gap = gridConfig.InitialGapPercent;
            gap = Math.Min(gap, gridConfig.MaxGapPercent);
            var reductionPercent = gridConfig.GapReductionPercent;
            reductionPercent = Math.Max(Math.Min(reductionPercent, 0.99), 0.5); // Ensure reduction percent is at least 1%
            if (type == OrderType.Buy)
            {
                gap = gap * Math.Pow(reductionPercent, gridNumber);                
            }
            else
            {
                gap = gap * Math.Pow(reductionPercent, gridNumber - 1);
            }
            gap = Math.Round(gap, 5);
            gap = Math.Max(gap, gridConfig.MinGapPercent);
            return gap;
        }
        public async Task<double> CalculatePriceAsync(int gridNumber, double price, OrderType type)
        {           
            var placedPrice = price;
            var gap = CalculateGapAsync(gridNumber, type);
            if(type == OrderType.Buy)
            {
                placedPrice = price * (1 - gap);
            }
            else
            {
                placedPrice = price * (1 + gap);
            }
            return placedPrice;
        }
    }
}
