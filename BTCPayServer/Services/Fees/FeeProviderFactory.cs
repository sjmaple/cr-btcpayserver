using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;

namespace BTCPayServer.Services.Fees;

public class FeeProviderFactory : IFeeProviderFactory
{
    public FeeProviderFactory(
        BTCPayServerEnvironment Environment,
        ExplorerClientProvider ExplorerClients,
        IHttpClientFactory HttpClientFactory,
        NBXplorerDashboard Dashboard,
        IMemoryCache MemoryCache)
    {
        _FeeProviders = new();

        // TODO: Pluginify this
        foreach ((var network, var client) in ExplorerClients.GetAll())
        {
            List<IFeeProvider> providers = new List<IFeeProvider>();
            providers.Add(new NBXplorerFeeProvider(client));
            if (network.IsBTC && Environment.NetworkType != ChainName.Regtest)
            {
                providers.Add(new MempoolSpaceFeeProvider(
                    MemoryCache,
                    $"MempoolSpaceFeeProvider-{network.CryptoCode}",
                    HttpClientFactory,
                    network is BTCPayNetwork n &&
                    n.NBitcoinNetwork.ChainName == ChainName.Testnet));
            }
            providers.Add(new StaticFeeProvider(new FeeRate(100L, 1)));
            var fallback = new FallbackFeeProvider(providers.ToArray());

            _FeeProviders.Add(network, new ClampFeeProvider(fallback, Dashboard, network.CryptoCode));
        }
    }

    class ClampFeeProvider(IFeeProvider inner, NBXplorerDashboard dashboard, string cryptoCode) : IFeeProvider
    {
        public IFeeProvider Inner { get; } = inner;

        public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
        {
            var rate = await Inner.GetFeeRateAsync(blockTarget);
            var d = dashboard.Get(cryptoCode);
            var min = d?.MempoolInfo?.MempoolMinfeeRate;
            min ??= d?.Status?.BitcoinStatus?.MinRelayTxFee;
            if (min is not null && rate < min)
                rate = min;
            rate = FeeRate.Min(rate, new FeeRate(2000m));
            return rate;
        }
    }

    private readonly Dictionary<BTCPayNetworkBase, IFeeProvider> _FeeProviders;
    public IFeeProvider CreateFeeProvider(BTCPayNetworkBase network)
    {
        return _FeeProviders.TryGetValue(network, out var prov) ? prov : throw new NotSupportedException($"No fee provider for this network ({network.CryptoCode})");
    }

}
