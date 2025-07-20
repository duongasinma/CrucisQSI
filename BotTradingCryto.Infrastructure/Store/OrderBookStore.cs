using BotTradingCrypto.Domain;
using BotTradingCryto.Application;
using BotTradingCryto.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCryto.Infrastructure
{
    public class OrderBookStore : IOrderBookStore
    {
        public Task<OperationResult> DeleteOrderBook(Guid id)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<OrderBook>> GetAllOrderBook()
        {
            throw new NotImplementedException();
        }

        public Task<GridOrder> GetGridOrderAsync(long id)
        {
            throw new NotImplementedException();
        }

        public Task<OrderBook> GetOrderBookAsync(Guid id)
        {
            throw new NotImplementedException();
        }

        public Task<OrderBook> GetOrderBookByOrderIdAsync(long id)
        {
            throw new NotImplementedException();
        }

        public Task<OperationResult> InsertOrderBook(OrderBook book)
        {
            throw new NotImplementedException();
        }

        public Task<OperationResult> UpdateOrderBook(OrderBook book)
        {
            throw new NotImplementedException();
        }
    }
}
