using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCryto.Domain.Utilities.Enums
{
    public enum OrderStatus
    {
        New,
        PartiallyFilled,
        Filled,
        Canceled,
        Rejected,
        Expired
    }
}
