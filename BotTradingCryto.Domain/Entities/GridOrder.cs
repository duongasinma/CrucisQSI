using BotTradingCrypto.Domain;
using BotTradingCrypto.Domain.Utilities.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Domain
{
    public class GridOrder
    {
        public long Id { get; set; }
        public OrderType Side { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public double Quantity { get; set; }
        public int GridLevel { get; set; }
        public double GapPercent { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public OrderStatus Status { get; set; } = OrderStatus.New;

    }
}
