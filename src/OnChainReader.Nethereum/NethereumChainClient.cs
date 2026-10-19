using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using OnChainReader.Core;

namespace OnChainReader.Nethereum;

/// <summary>Read-only <see cref="IChainClient"/> over any Ethereum JSON-RPC endpoint (Infura, Alchemy, a local node…). Never holds a key.</summary>
public sealed class NethereumChainClient : IChainClient
{
    /// <summary>keccak256("Transfer(address,address,uint256)")</summary>
    public const string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    /// <summary>First 4 bytes of keccak256("symbol()")</summary>
    private const string SymbolSelector = "0x95d89b41";

    private readonly Web3 _web3;

    public NethereumChainClient(string rpcUrl)
    {
        if (!Uri.TryCreate(rpcUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("rpcUrl must be an absolute http(s) URL", nameof(rpcUrl));
        _web3 = new Web3(rpcUrl); // no account: read-only
    }

    [Function("decimals", "uint8")]
    private sealed class DecimalsFunction : FunctionMessage;

    [Function("balanceOf", "uint256")]
    private sealed class BalanceOfFunction : FunctionMessage
    {
        [Parameter("address", "_owner", 1)] public string Owner { get; set; } = "";
    }

    public Task<ulong> GetBlockNumberAsync(CancellationToken ct) =>
        Wrap(async () => (ulong)(await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()).Value);

    public Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct) =>
        Wrap(async () => (await _web3.Eth.GetBalance.SendRequestAsync(address)).Value);

    public Task<bool> IsContractAsync(string address, CancellationToken ct) =>
        Wrap(async () => LooksLikeContract(await _web3.Eth.GetCode.SendRequestAsync(address)));

    /// <summary>Contract code is any non-empty bytecode EXCEPT an EIP-7702 delegation designator (<c>0xef0100</c> + 20-byte address), which marks a normal wallet that delegates its code (seen on mainnet, e.g. vitalik.eth).</summary>
    public static bool LooksLikeContract(string? code) =>
        !string.IsNullOrEmpty(code) && code != "0x" && code != "0x0" && !code.StartsWith("0xef0100", StringComparison.OrdinalIgnoreCase);

    public Task<int> GetTokenDecimalsAsync(string token, CancellationToken ct) =>
        Wrap(async () => (int)await _web3.Eth.GetContractQueryHandler<DecimalsFunction>().QueryAsync<byte>(token, new DecimalsFunction()));

    public async Task<string?> GetTokenSymbolAsync(string token, CancellationToken ct)
    {
        try
        {
            var data = await _web3.Eth.Transactions.Call.SendRequestAsync(new CallInput { To = token, Data = SymbolSelector });
            return Amounts.DecodeSymbol(data);
        }
        catch (Exception) // a token without symbol() is still a valid token; the symbol is cosmetic
        {
            return null;
        }
    }

    public Task<BigInteger> GetTokenBalanceAsync(string token, string holder, CancellationToken ct) =>
        Wrap(async () => await _web3.Eth.GetContractQueryHandler<BalanceOfFunction>().QueryAsync<BigInteger>(token, new BalanceOfFunction { Owner = holder }));

    public Task<IReadOnlyList<RawLog>> GetTransferLogsAsync(string token, ulong fromBlock, ulong toBlock, CancellationToken ct) =>
        Wrap<IReadOnlyList<RawLog>>(async () =>
        {
            var filter = new NewFilterInput
            {
                Address = [token],
                Topics = [TransferTopic],
                FromBlock = new BlockParameter(new HexBigInteger(fromBlock)),
                ToBlock = new BlockParameter(new HexBigInteger(toBlock)),
            };
            var logs = await _web3.Eth.Filters.GetLogs.SendRequestAsync(filter);
            return logs.Select(l => new RawLog(
                l.Address, l.Topics.Select(t => t?.ToString() ?? "").ToList(), l.Data ?? "0x",
                (ulong)l.BlockNumber.Value, l.TransactionHash, (int)l.LogIndex.Value)).ToList();
        });

    private static async Task<T> Wrap<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or global::Nethereum.JsonRpc.Client.RpcClientUnknownException
                                      or global::Nethereum.JsonRpc.Client.RpcResponseException or global::Nethereum.JsonRpc.Client.RpcClientTimeoutException)
        {
            throw new UpstreamException("the Ethereum node could not answer: " + e.Message, e);
        }
        catch (Exception e) when (e is global::Nethereum.ABI.FunctionEncoding.SmartContractRevertException or InvalidOperationException or FormatException or ArgumentException)
        {
            // eth_call on an address that is not an ERC-20 returns empty data → decoding fails
            throw new InvalidRequestException("the address does not look like an ERC-20 token contract: " + e.Message);
        }
    }
}
