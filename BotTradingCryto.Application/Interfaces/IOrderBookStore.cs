using BotTradingCryto.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCryto.Application
{
    public interface IOrderBookStore
    {
        Task<OrderBook> GetOrderBookAsync(Guid id);
        Task<IEnumerable<OrderBook>> GetAllOrderBook();
        Task<OperationResult> InsertOrderBook(OrderBook book);
        Task<OperationResult> UpdateOrderBook(OrderBook book);
        Task<OperationResult> DeleteOrderBook(Guid id);
    }
}
