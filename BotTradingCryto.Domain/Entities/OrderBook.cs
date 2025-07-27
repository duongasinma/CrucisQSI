using BotTradingCrypto.Domain;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Domain
{
    public class OrderBook
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        public string Symbol { get; set; } = null!;
        public string? SubscriptionId { get; set; }
        public int StepSize { get; set; } = 8;
        public int StickSize { get; set; } = 3;
        public List<GridOrder> GridOrders { get; set; } = new List<GridOrder>();
        public OrderBookDetail OrderBookDetail { get; set; } = new OrderBookDetail();
    }
    public class  OrderBookDetail
    {
        public double BaseQuantity { get; set; }
        public double QuantityIncrement { get; set; }
        public int TotalGrid { get; set; }
        public double InitialGapPercent { get; set; }
        public double GapReductionPercent { get; set; }
        public double MaxGapPercent { get; set; }
        public double MinGapPercent { get; set; }
        public double ResetGridPercent { get; set; }
        public double ResetIncrementPercent { get; set; }
        public double CompoundGrowthRate { get; set; } = 0.0;
    }
}
