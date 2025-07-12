using BotTradingCryto.Domain;
using BotTradingCryto.Domain.Utilities.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Domain
{
    public class GridOrder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public OrderType Side { get; set; }
        public double Price { get; set; }
        public double Quantity { get; set; }
        public int GridLevel { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public OrderStatus Status { get; set; } = OrderStatus.New;

    }
}
