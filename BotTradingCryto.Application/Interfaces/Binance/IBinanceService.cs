
using BotTradingCrypto;
using BotTradingCrypto.Application;

namespace BotTradingCrypto.Application
{
    public interface IBinanceService : IBaseCryptoService
    {
        Task<OperationResult> PlaceSpotLimitOrderAsync(int num, string symbol, decimal price, decimal quantity, bool isBuy);
        Task ConnectSocketTradingAsync(string symbol, int num);
        Task CancelAllOrderAsync(string symbol);
        Task<int> SubscribeMiniTickerAsync(string symbol, Func<double, string, Task> onData, string orderBookId,CancellationToken ct = default);
        Task<OperationResult> SubscribeUserDataAsync(string symbol, Func<long, Task> onData, string orderBookId, CancellationToken ct = default);
        Task UnsubscribeMiniTickerAsync(int subscribeId);
        Task UnsubscribeUserDataAsync();
        Task<int> GetTickSize(string symbol);
        Task<int> GetStepSize(string symbol);
        Task TrackingTickerAsync();
    }
}