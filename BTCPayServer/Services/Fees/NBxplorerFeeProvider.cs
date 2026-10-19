#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Services.Fees
{
    public class NBXplorerFeeProvider(
        ExplorerClient explorerClient,
        IMemoryCache memoryCache,
        string cacheKey) : IFeeProvider
    {
        private readonly TimeSpan _expiration = TimeSpan.FromMinutes(25);

        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            if (memoryCache.TryGetValue(GetCacheKey(blockTarget), out FeeRate? cachedFeeRate) && cachedFeeRate is not null)
                return cachedFeeRate;

            var feeRate = await FetchFeeRate(blockTarget, CancellationToken.None);
            CacheFeeRate(blockTarget, feeRate);
            return feeRate;
        }

        public Task RefreshCache(IEnumerable<int> blockTargets, CancellationToken cancellationToken)
        {
            return Task.WhenAll(blockTargets.Select(async blockTarget =>
            {
                try
                {
                    var feeRate = await FetchFeeRate(blockTarget, cancellationToken);
                    CacheFeeRate(blockTarget, feeRate);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    // Keep the previous estimate until it expires.
                }
            }));
        }

        private async Task<FeeRate> FetchFeeRate(int blockTarget, CancellationToken cancellationToken) =>
            (await explorerClient.GetFeeRateAsync(blockTarget, cancellationToken).ConfigureAwait(false)).FeeRate;

        private void CacheFeeRate(int blockTarget, FeeRate feeRate) =>
            memoryCache.Set(GetCacheKey(blockTarget), feeRate, _expiration);

        private string GetCacheKey(int blockTarget) => $"{cacheKey}-{blockTarget}";
    }
}
