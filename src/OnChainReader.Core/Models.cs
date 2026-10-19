using System.Numerics;

namespace OnChainReader.Core;

/// <summary>An ETH (native coin) balance. <see cref="Wei"/> is exact; <see cref="Eth"/> is the same value as a decimal string (no float rounding).</summary>
public sealed record NativeBalance(string Address, BigInteger Wei, string Eth, ulong BlockNumber);

/// <summary>Static facts about an ERC-20 token contract.</summary>
public sealed record TokenInfo(string Address, string? Symbol, int Decimals);

/// <summary>An ERC-20 balance: <see cref="Raw"/> is the on-chain integer, <see cref="Formatted"/> divides it by 10^decimals exactly.</summary>
public sealed record TokenBalance(string Token, string? Symbol, int Decimals, string Holder, BigInteger Raw, string Formatted, ulong BlockNumber);

/// <summary>One ERC-20 <c>Transfer(address indexed from, address indexed to, uint256 value)</c> event.</summary>
public sealed record TransferEvent(string Token, string From, string To, BigInteger Value, string FormattedValue, ulong BlockNumber, string TransactionHash, int LogIndex);

/// <summary>Transfer events of one query. <see cref="Truncated"/> is true when more than <see cref="ChainReaderService.MaxEvents"/> matched: narrow the block range or add a from/to filter.</summary>
public sealed record TransferPage(string Token, ulong FromBlock, ulong ToBlock, IReadOnlyList<TransferEvent> Events, bool Truncated);

/// <summary>A raw log as returned by an Ethereum JSON-RPC node (<c>eth_getLogs</c>).</summary>
public sealed record RawLog(string Address, IReadOnlyList<string> Topics, string Data, ulong BlockNumber, string TransactionHash, int LogIndex);

/// <summary>Thrown for caller mistakes (bad address, block range too large…). The API maps it to HTTP 400.</summary>
public sealed class InvalidRequestException(string message) : Exception(message);

/// <summary>Thrown when the node cannot be reached or answers with an error. The API maps it to HTTP 502.</summary>
public sealed class UpstreamException(string message, Exception? inner = null) : Exception(message, inner);
