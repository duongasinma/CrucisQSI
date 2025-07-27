
using BotTradingCrypto;
using BotTradingCrypto.Application;

namespace BotTradingCrypto.Application
{
    public interface IBinanceService : IBaseCryptoService
    {
        Task<OperationResult> PlaceSpotLimitOrderAsync(string symbol, decimal price, decimal quantity, bool isBuy);
        Task ConnectSocketTradingAsync(string symbol, int num);
        Task CancelAllOrderAsync();
        Task<int> SubscribeMiniTickerAsync(string symbol, Action<double, string> onData, string orderBookId,CancellationToken ct = default);
        Task<OperationResult> SubscribeUserDataAsync(string symbol, Action<long> onData, string orderBookId, CancellationToken ct = default);
        Task UnsubscribeMiniTickerAsync(int subscribeId);
        Task UnsubscribeUserDataAsync();
        Task<int> GetTickSize(string symbol);
        Task<int> GetStepSize(string symbol);
    }
}