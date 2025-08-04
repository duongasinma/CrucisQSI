using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BotTradingCrypto.Domain
{
    public class LockProvider
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        public SemaphoreSlim GetLock(string id)
        {
            return _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        }
        // Optional: Cleanup method if you want to remove locks when no longer needed
        public void RemoveLock(string id)
        {
            _locks.TryRemove(id, out _);
        }
    }
}
