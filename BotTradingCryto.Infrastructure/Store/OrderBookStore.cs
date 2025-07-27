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
        public Task<OperationResult> DeleteOrderBook(string id)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<OrderBook>> GetAllOrderBook()
        {
            var list = await _orderBookCollection.Find(_ => true)
                .ToListAsync();
            if (!list.Any())
            {
                Console.WriteLine("List don't have any value");
            }
                return list;
        }

        public Task<GridOrder> GetGridOrderAsync(long id)
        {
            throw new NotImplementedException();
        }

        public Task<OrderBook> GetOrderBookAsync(string id)
        {
            throw new NotImplementedException();
        }

        public Task<OrderBook> GetOrderBookByOrderIdAsync(long id)
        {
            throw new NotImplementedException();
        }

        public async Task<OperationResult> InsertOrderBook(OrderBook book)
        {
            await _orderBookCollection.InsertOneAsync(book);
            return OperationResult.Success;
        }

        public Task<OperationResult> UpdateOrderBook(OrderBook book)
        {
            throw new NotImplementedException();
        }
    }
}
