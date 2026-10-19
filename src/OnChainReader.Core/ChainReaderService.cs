using System.Numerics;

namespace OnChainReader.Core;

/// <summary>Read-only queries over <see cref="IChainClient"/>. No keys, no signing, no state changes.</summary>
public sealed class ChainReaderService(IChainClient chain)
{
    /// <summary>Largest block window one logs query may span; public nodes reject big ranges, and a bounded range keeps the API cheap.</summary>
    public const ulong MaxLogBlockRange = 2_000;

    /// <summary>Most events returned per request.</summary>
    public const int MaxEvents = 1_000;

    public async Task<NativeBalance> GetEthBalanceAsync(string address, CancellationToken ct)
    {
        var addr = AddressValidator.Normalize(address);
        var block = await chain.GetBlockNumberAsync(ct);
        var wei = await chain.GetBalanceAsync(addr, ct);
        return new NativeBalance(addr, wei, Amounts.Format(wei, Amounts.EthDecimals), block);
    }

    public async Task<TokenBalance> GetTokenBalanceAsync(string token, string holder, CancellationToken ct)
    {
        var tokenAddr = AddressValidator.Normalize(token);
        var holderAddr = AddressValidator.Normalize(holder);
        await RequireContractAsync(tokenAddr, ct);
        var block = await chain.GetBlockNumberAsync(ct);
        var decimals = await chain.GetTokenDecimalsAsync(tokenAddr, ct);
        var symbol = await chain.GetTokenSymbolAsync(tokenAddr, ct);
        var raw = await chain.GetTokenBalanceAsync(tokenAddr, holderAddr, ct);
        return new TokenBalance(tokenAddr, symbol, decimals, holderAddr, raw, Amounts.Format(raw, decimals), block);
    }

    /// <summary>Transfer events of <paramref name="token"/> in [fromBlock, toBlock], optionally filtered to one sender and/or receiver.</summary>
    public async Task<TransferPage> GetTransfersAsync(
        string token, ulong fromBlock, ulong toBlock, string? from, string? to, CancellationToken ct)
    {
        var tokenAddr = AddressValidator.Normalize(token);
        if (toBlock < fromBlock) throw new InvalidRequestException("toBlock must be >= fromBlock");
        if (toBlock - fromBlock >= MaxLogBlockRange) throw new InvalidRequestException($"block range too large; at most {MaxLogBlockRange} blocks per request");
        var fromFilter = from is null ? null : AddressValidator.Normalize(from);
        var toFilter = to is null ? null : AddressValidator.Normalize(to);

        await RequireContractAsync(tokenAddr, ct);
        var decimals = await chain.GetTokenDecimalsAsync(tokenAddr, ct);
        var logs = await chain.GetTransferLogsAsync(tokenAddr, fromBlock, toBlock, ct);

        var events = new List<TransferEvent>();
        var truncated = false;
        foreach (var log in logs)
        {
            // A real ERC-20 Transfer has 3 topics (signature, from, to) and the value in data; ERC-721 reuses the signature with 4 topics - skip those.
            if (log.Topics.Count != 3) continue;
            string logFrom, logTo;
            BigInteger value;
            try
            {
                logFrom = Amounts.AddressFromTopic(log.Topics[1]);
                logTo = Amounts.AddressFromTopic(log.Topics[2]);
                value = Amounts.ParseHexUnsigned(log.Data);
            }
            catch (FormatException)
            {
                continue; // a malformed log from an odd contract must not break the whole response
            }
            if (fromFilter is not null && logFrom != fromFilter) continue;
            if (toFilter is not null && logTo != toFilter) continue;
            if (events.Count >= MaxEvents) { truncated = true; break; }
            events.Add(new TransferEvent(tokenAddr, logFrom, logTo, value, Amounts.Format(value, decimals), log.BlockNumber, log.TransactionHash, log.LogIndex));
        }
        return new TransferPage(tokenAddr, fromBlock, toBlock, events.OrderBy(e => e.BlockNumber).ThenBy(e => e.LogIndex).ToList(), truncated);
    }

    private async Task RequireContractAsync(string address, CancellationToken ct)
    {
        // Without this, a wallet address passed as a token silently answers decimals=0, balance=0 (found while testing against a live node).
        if (!await chain.IsContractAsync(address, ct))
            throw new InvalidRequestException("no contract code at this address; it is not an ERC-20 token");
    }
}
