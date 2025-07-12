using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Domain
{
    public class GridConfiguration
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
    }
}
