using BotTradingCrypto.Domain;
using BotTradingCrypto.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Application
{
    public interface IOrderBookStore
    {
        Task<OrderBook> GetOrderBookAsync(Guid id);
        Task<OrderBook> GetOrderBookByOrderIdAsync(long id);
        Task<IEnumerable<OrderBook>> GetAllOrderBook();
        Task<OperationResult> InsertOrderBook(OrderBook book);
        Task<OperationResult> UpdateOrderBook(OrderBook book);
        Task<OperationResult> DeleteOrderBook(Guid id);

        Task<GridOrder> GetGridOrderAsync(long id);
    }
}
