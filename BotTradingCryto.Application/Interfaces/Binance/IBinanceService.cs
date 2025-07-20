
using BotTradingCryto;
using BotTradingCryto.Application;

namespace BotTradingCrypto.Application
{
    public interface IBinanceService : IBaseCryptoService
    {
        Task<OperationResult> GetSymbolInfoAsync(string symbol);
        Task<OperationResult> PlaceSpotLimitOrderAsync(string symbol, decimal price, decimal quantity, bool isBuy);
        Task ConnectSocketTradingAsync(string symbol, int num);
        Task CancelAllOrderAsync();
        Task<int> SubscribeMiniTickerAsync(string symbol, Action<double, Guid> onData, Guid orderBookId,CancellationToken ct = default);
        Task<OperationResult> SubscribeUserDataAsync(string symbol, Action<long> onData, Guid orderBookId, CancellationToken ct = default);
        Task UnsubscribeMiniTickerAsync(int subscribeId);
        Task UnsubscribeUserDataAsync();
        Task<int> GetTickSize(string symbol);
        Task<int> GetStepSize(string symbol);
    }
}