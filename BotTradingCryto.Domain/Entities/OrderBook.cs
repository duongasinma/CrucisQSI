using BotTradingCrypto.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCryto.Domain
{
    public class OrderBook
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Symbol { get; set; }
        public List<GridOrder> gridOrders { get; set; } = new List<GridOrder>();
        public string? SubscriptionId { get; set; }
    }
}
