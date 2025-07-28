using BotTradingCrypto.Domain;
using BotTradingCrypto.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver;
using Microsoft.Extensions.Options;

namespace BotTradingCrypto.Infrastructure
{
    public class OrderBookStore : IOrderBookStore
    {
        private readonly IMongoCollection<OrderBook> _orderBookCollection;
        public OrderBookStore(
            IOptionsSnapshot<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient
            )
        {
            var database = mongoClient.GetDatabase(mongoDbSettings.Value.DatabaseName);
            _orderBookCollection = database.GetCollection<OrderBook>(mongoDbSettings.Value.OrderBookCollectionName);
        }
        public async Task<IEnumerable<OrderBook>> GetAllOrderBook()
        {
            return await _orderBookCollection
                .Find(_ => true)
                .ToListAsync()
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        return t.Result.AsEnumerable();
                    }
                    else
                    {
                        throw new Exception("Failed to retrieve order books", t.Exception);
                    }
                });
        }

        public async Task<GridOrder> GetGridOrderAsync(long id)
        {
            var order = await _orderBookCollection
                .Find(book => book.GridOrders.Any(order => order.Id == id))
                .Project(book => book.GridOrders.FirstOrDefault(order => order.Id == id))
                .FirstOrDefaultAsync();
            if (order == null)
            {
                return new GridOrder(); // or throw an exception if you prefer
            }
            return order;
        }

        public async Task<OrderBook> GetOrderBookAsync(string id)
        {
            var book = await _orderBookCollection
                .Find(x => x.Id == id)
                .FirstOrDefaultAsync();
            return book;
        }

        public async Task<OrderBook> GetOrderBookByOrderIdAsync(long id)
        {
            var book = await _orderBookCollection
                .Find(book => book.GridOrders.Any(order => order.Id == id))
                //.Project(book => book.GridOrders.FirstOrDefault(order => order.Id == id))
                .FirstOrDefaultAsync();
            if (book == null)
            {
                return new OrderBook(); // or throw an exception if you prefer
            }
            return book;
        }

        public async Task<OperationResult> InsertOrderBook(OrderBook book)
        {
            return await _orderBookCollection
                .InsertOneAsync(book)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        return OperationResult.Success;
                    }
                    else
                    {
                        return OperationResult.Failed(t.Exception, "Failed to insert order book");
                    }
                });
        }

        public async Task<OperationResult> UpdateOrderBook(OrderBook book)
        {
            return await _orderBookCollection
                .ReplaceOneAsync(
                    filter: x => x.Id == book.Id,
                    replacement: book,
                    options: new ReplaceOptions { IsUpsert = true }
                ).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        return OperationResult.Success;
                    }
                    else
                    {
                        return OperationResult.Failed(t.Exception, "Failed to update order book");
                    }
                });
        }
        public Task<OperationResult> DeleteOrderBook(string id)
        {
            return _orderBookCollection
                .DeleteOneAsync(x => x.Id == id)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        return OperationResult.Success;
                    }
                    else
                    {
                        return OperationResult.Failed(t.Exception, "Failed to delete order book");
                    }
                });
        }

    }
}
