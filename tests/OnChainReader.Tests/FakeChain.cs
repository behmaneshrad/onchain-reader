using System.Numerics;
using OnChainReader.Core;

namespace OnChainReader.Tests;

/// <summary>In-memory node. Counts calls so tests can prove that invalid input never reaches the network.</summary>
public sealed class FakeChain : IChainClient
{
    public ulong BlockNumber { get; set; } = 1;
    public BigInteger EthBalance { get; set; }
    public bool IsContract { get; set; } = true;
    public int Decimals { get; set; } = 18;
    public string? Symbol { get; set; } = "TKN";
    public BigInteger TokenBalance { get; set; }
    public List<RawLog> Logs { get; set; } = [];
    public Exception? Fail { get; set; }
    public int Calls { get; private set; }

    private T Hit<T>(T value)
    {
        Calls++;
        if (Fail is not null) throw Fail;
        return value;
    }

    public Task<ulong> GetBlockNumberAsync(CancellationToken ct) => Task.FromResult(Hit(BlockNumber));
    public Task<BigInteger> GetBalanceAsync(string address, CancellationToken ct) => Task.FromResult(Hit(EthBalance));
    public Task<bool> IsContractAsync(string address, CancellationToken ct) => Task.FromResult(Hit(IsContract));
    public Task<int> GetTokenDecimalsAsync(string token, CancellationToken ct) => Task.FromResult(Hit(Decimals));
    public Task<string?> GetTokenSymbolAsync(string token, CancellationToken ct) => Task.FromResult(Hit(Symbol));
    public Task<BigInteger> GetTokenBalanceAsync(string token, string holder, CancellationToken ct) => Task.FromResult(Hit(TokenBalance));
    public Task<IReadOnlyList<RawLog>> GetTransferLogsAsync(string token, ulong fromBlock, ulong toBlock, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RawLog>>(Hit(Logs.Where(l => l.BlockNumber >= fromBlock && l.BlockNumber <= toBlock).ToList()));
}
