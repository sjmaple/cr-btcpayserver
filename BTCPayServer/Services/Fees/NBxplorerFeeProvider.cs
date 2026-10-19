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

        public async Task RefreshCache(IEnumerable<int> blockTargets, CancellationToken cancellationToken)
        {
            var batch = explorerClient.RPCClient.PrepareBatch();
            var estimates = blockTargets
                .Distinct()
                .Select(blockTarget =>
                    (BlockTarget: blockTarget,
                        Estimate: batch.TryEstimateSmartFeeAsync(blockTarget, cancellationToken: cancellationToken)))
                .ToArray();

            try
            {
                await batch.SendBatchAsync(cancellationToken);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (var estimate in estimates)
            {
                try
                {
                    if (await estimate.Estimate is { FeeRate: { } feeRate })
                        CacheFeeRate(estimate.BlockTarget, feeRate);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    // Keep the previous estimate until it expires.
                }
            }
        }

        private async Task<FeeRate> FetchFeeRate(int blockTarget, CancellationToken cancellationToken) =>
            (await explorerClient.RPCClient.TryEstimateSmartFeeAsync(blockTarget, cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.FeeRate ??
            throw new InvalidOperationException("Fee estimation unavailable");

        private void CacheFeeRate(int blockTarget, FeeRate feeRate) =>
            memoryCache.Set(GetCacheKey(blockTarget), feeRate, _expiration);

        private string GetCacheKey(int blockTarget) => $"{cacheKey}-{blockTarget}";
    }
}
