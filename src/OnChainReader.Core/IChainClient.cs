using System.Numerics;

namespace OnChainReader.Core;

/// <summary>The only thing the service needs from a node. The Nethereum adapter implements it; tests use an in-memory fake.</summary>
public interface IChainClient
{
    Task<ulong> GetBlockNumberAsync(CancellationToken ct);

    /// <summary>ETH balance in wei at the latest block.</summary>
    Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct);

    /// <summary>True when the address holds contract code (<c>eth_getCode</c> is not "0x"). A plain wallet answers every ERC-20 call with empty data.</summary>
    Task<bool> IsContractAsync(string address, CancellationToken ct);

    /// <summary>ERC-20 <c>decimals()</c>.</summary>
    Task<int> GetTokenDecimalsAsync(string token, CancellationToken ct);

    /// <summary>ERC-20 <c>symbol()</c>; null if the contract has none or returns a non-standard value.</summary>
    Task<string?> GetTokenSymbolAsync(string token, CancellationToken ct);

    /// <summary>ERC-20 <c>balanceOf(holder)</c>.</summary>
    Task<BigInteger> GetTokenBalanceAsync(string token, string holder, CancellationToken ct);

    /// <summary><c>eth_getLogs</c> for Transfer events of one token between two blocks (inclusive).</summary>
    Task<IReadOnlyList<RawLog>> GetTransferLogsAsync(string token, ulong fromBlock, ulong toBlock, CancellationToken ct);
}
