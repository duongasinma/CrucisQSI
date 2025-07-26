using BotTradingCrypto.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Application
{
    public interface ISpotGridTradingService
    {
        Task StartGridTradingAsync(string symbol, OrderBookDetail orderBookDetail);
        Task<bool> StopGridTradingAsync(int subId);      
    }
}
