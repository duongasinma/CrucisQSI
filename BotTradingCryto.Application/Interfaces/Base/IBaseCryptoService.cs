using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Application
{
    public interface IBaseCryptoService
    {
        Task<OperationResult> GetAccoutInfoAsync();
        Task<OperationResult> GetSymbolInfoAsync(string symbol);
        Task<double> GetCurrentPriceAsync(string symbol);
        Task<double> GetTradingFeeAsynce(string symbol);
    }
}
