#nullable enable
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Fees;

public class BitcoinFeeProvider(
    ExplorerClient explorerClient,
    MempoolSpaceFeeProvider mempoolSpaceFeeProvider,
    NBXplorerFeeProvider nbXplorerFeeProvider) : IFeeProvider
{
    private const int BitcoinCore32 = 320000;
    private int? _BitcoinCoreVersion;

    public async Task<FeeRate> GetFeeRateAsync(int blockTarget = 20)
    {
        if (await UseNBXplorer())
            return await nbXplorerFeeProvider.GetFeeRateAsync(blockTarget);

        try
        {
            return await mempoolSpaceFeeProvider.GetFeeRateAsync(blockTarget);
        }
        catch
        {
            return await nbXplorerFeeProvider.GetFeeRateAsync(blockTarget);
        }
    }

    public async Task RefreshCache()
    {
        if (!await UseNBXplorer())
            await mempoolSpaceFeeProvider.RefreshCache();
    }

    private async Task<bool> UseNBXplorer()
    {
        if (_BitcoinCoreVersion is null)
        {
            try
            {
                var networkInfo = await explorerClient.RPCClient.SendCommandAsync(new RPCRequest("getnetworkinfo", [])
                {
                    ThrowIfRPCError = false
                });
                if (networkInfo.Error is null)
                    _BitcoinCoreVersion = networkInfo.Result["version"]?.Value<int>();
            }
            catch
            {
                // Keep using the existing mempool.space estimator until the node version can be detected.
            }
        }

        return _BitcoinCoreVersion is >= BitcoinCore32;
    }
}
