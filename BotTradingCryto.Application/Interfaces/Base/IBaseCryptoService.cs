using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCryto.Application
{
    public interface IBaseCryptoService
    {
        Task<double> GetCurrentPriceAsync(string symbol);
    }
}
