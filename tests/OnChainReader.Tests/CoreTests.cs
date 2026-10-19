using System.Numerics;
using OnChainReader.Core;

namespace OnChainReader.Tests;

public class AmountsTests
{
    [Theory]
    [InlineData("0", 18, "0")]
    [InlineData("1000000000000000000", 18, "1")]
    [InlineData("1500000000000000000", 18, "1.5")]
    [InlineData("1", 18, "0.000000000000000001")]
    [InlineData("123456789", 6, "123.456789")]
    [InlineData("100", 6, "0.0001")]
    [InlineData("42", 0, "42")]
    [InlineData("-1500000", 6, "-1.5")]
    public void Format_is_exact(string raw, int decimals, string expected) =>
        Assert.Equal(expected, Amounts.Format(BigInteger.Parse(raw), decimals));

    [Fact]
    public void Format_handles_values_above_double_precision() =>
        // 2^256-1 has 78 digits; a double would lose all but ~15
        Assert.Equal("115792089237316195423570985008687907853269984665640564039457.584007913129639935",
            Amounts.Format(BigInteger.Pow(2, 256) - 1, 18));

    [Fact]
    public void Format_rejects_absurd_decimals() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Amounts.Format(BigInteger.One, 78));

    [Theory]
    [InlineData("0x", "0")]
    [InlineData("0x0", "0")]
    [InlineData("0xff", "255")]
    [InlineData("0x00000000000000000000000000000000000000000000000000000000000003e8", "1000")]
    [InlineData("0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "115792089237316195423570985008687907853269984665640564039457584007913129639935")]
    public void ParseHexUnsigned_never_reads_the_top_bit_as_a_sign(string hex, string expected) =>
        Assert.Equal(BigInteger.Parse(expected), Amounts.ParseHexUnsigned(hex));

    [Fact]
    public void ParseHexUnsigned_rejects_garbage() => Assert.Throws<FormatException>(() => Amounts.ParseHexUnsigned("0xzz"));

    [Fact]
    public void AddressFromTopic_takes_the_low_20_bytes() =>
        Assert.Equal("0xd8da6bf26964af9d7eed9e03e53415d37aa96045",
            Amounts.AddressFromTopic("0x000000000000000000000000d8da6bf26964af9d7eed9e03e53415d37aa96045"));

    [Fact]
    public void AddressFromTopic_rejects_wrong_length() => Assert.Throws<FormatException>(() => Amounts.AddressFromTopic("0x1234"));

    [Fact]
    public void DecodeSymbol_reads_a_dynamic_string()
    {
        // abi.encode("USDC")
        var data = "0x" + "0000000000000000000000000000000000000000000000000000000000000020"
                        + "0000000000000000000000000000000000000000000000000000000000000004"
                        + "5553444300000000000000000000000000000000000000000000000000000000";
        Assert.Equal("USDC", Amounts.DecodeSymbol(data));
    }

    [Fact]
    public void DecodeSymbol_reads_a_bytes32_symbol_like_MKR()
    {
        var data = "0x4d4b520000000000000000000000000000000000000000000000000000000000";
        Assert.Equal("MKR", Amounts.DecodeSymbol(data));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("0x1234")]
    [InlineData("not hex")]
    public void DecodeSymbol_returns_null_for_unreadable_data(string? data) => Assert.Null(Amounts.DecodeSymbol(data));
}

public class ContractDetectionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0x", false)]
    [InlineData("0x0", false)]
    [InlineData("0xef01005a7fc11397e9a8ad41bf10bf13f22b0a63f96f6d", false)]   // EIP-7702 delegation: still a wallet
    [InlineData("0xEF01005a7fc11397e9a8ad41bf10bf13f22b0a63f96f6d", false)]
    [InlineData("0x6080604052", true)]
    public void Only_real_bytecode_counts_as_a_contract(string? code, bool expected) =>
        Assert.Equal(expected, OnChainReader.Nethereum.NethereumChainClient.LooksLikeContract(code));
}

public class AddressValidatorTests
{
    private const string Vitalik = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045"; // EIP-55 checksummed

    [Fact]
    public void Checksummed_address_is_accepted_and_lowercased() =>
        Assert.Equal(Vitalik.ToLowerInvariant(), AddressValidator.Normalize(Vitalik));

    [Fact]
    public void All_lowercase_and_all_uppercase_are_accepted()
    {
        Assert.Equal(Vitalik.ToLowerInvariant(), AddressValidator.Normalize(Vitalik.ToLowerInvariant()));
        Assert.Equal(Vitalik.ToLowerInvariant(), AddressValidator.Normalize("0x" + Vitalik[2..].ToUpperInvariant()));
    }

    [Fact]
    public void A_typo_in_a_checksummed_address_is_rejected()
    {
        var typo = Vitalik[..^1] + (Vitalik[^1] == '5' ? '4' : '5');
        var ex = Assert.Throws<InvalidRequestException>(() => AddressValidator.Normalize(typo));
        Assert.Contains("checksum", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x123")]
    [InlineData("d8dA6BF26964aF9D7eEd9e03E53415D37aA96045")]
    [InlineData("0xZZdA6BF26964aF9D7eEd9e03E53415D37aA96045")]
    public void Malformed_addresses_are_rejected(string? address) =>
        Assert.Throws<InvalidRequestException>(() => AddressValidator.Normalize(address));
}

public class ChainReaderServiceTests
{
    private const string Token = "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48";
    private const string Alice = "0x1111111111111111111111111111111111111111";
    private const string Bob = "0x2222222222222222222222222222222222222222";

    private static string Topic(string address) => "0x000000000000000000000000" + address[2..];
    private static string Word(BigInteger v) => "0x" + v.ToString("x").PadLeft(64, '0');

    [Fact]
    public async Task Eth_balance_is_exact_and_carries_the_block()
    {
        var chain = new FakeChain { BlockNumber = 19_000_000, EthBalance = BigInteger.Parse("2500000000000000001") };
        var r = await new ChainReaderService(chain).GetEthBalanceAsync(Alice, CancellationToken.None);
        Assert.Equal("2.500000000000000001", r.Eth);
        Assert.Equal(19_000_000UL, r.BlockNumber);
        Assert.Equal(Alice, r.Address);
    }

    [Fact]
    public async Task Token_balance_uses_the_token_decimals_and_symbol()
    {
        var chain = new FakeChain { Decimals = 6, Symbol = "USDC", TokenBalance = 1_234_567_890 };
        var r = await new ChainReaderService(chain).GetTokenBalanceAsync(Token, Alice, CancellationToken.None);
        Assert.Equal("1234.56789", r.Formatted);
        Assert.Equal("USDC", r.Symbol);
        Assert.Equal(6, r.Decimals);
        Assert.Equal(new BigInteger(1_234_567_890), r.Raw);
    }

    [Fact]
    public async Task Transfers_are_parsed_filtered_and_ordered()
    {
        var chain = new FakeChain
        {
            Decimals = 6,
            Logs =
            [
                new RawLog(Token, ["0xddf", Topic(Bob), Topic(Alice)], Word(2_000_000), 102, "0xbbb", 1),
                new RawLog(Token, ["0xddf", Topic(Alice), Topic(Bob)], Word(1_500_000), 100, "0xaaa", 4),
                new RawLog(Token, ["0xddf", Topic(Alice), Topic(Bob), Word(7)], "0x", 101, "0xnft", 0),   // ERC-721 style (4 topics): skipped
                new RawLog(Token, ["0xddf", "0x1234", Topic(Bob)], Word(1), 103, "0xbad", 0),               // malformed topic: skipped, not fatal
            ],
        };
        var svc = new ChainReaderService(chain);

        var all = await svc.GetTransfersAsync(Token, 100, 110, null, null, CancellationToken.None);
        Assert.Equal(["0xaaa", "0xbbb"], all.Events.Select(e => e.TransactionHash).ToArray());
        Assert.Equal("1.5", all.Events[0].FormattedValue);
        Assert.False(all.Truncated);

        var fromAlice = await svc.GetTransfersAsync(Token, 100, 110, Alice, null, CancellationToken.None);
        Assert.Single(fromAlice.Events);
        Assert.Equal(Bob, fromAlice.Events[0].To);

        var toAlice = await svc.GetTransfersAsync(Token, 100, 110, null, Alice.ToUpperInvariant().Replace("0X", "0x"), CancellationToken.None);
        Assert.Single(toAlice.Events);
        Assert.Equal("2", toAlice.Events[0].FormattedValue);
    }

    [Fact]
    public async Task A_too_large_block_range_is_a_caller_error_and_never_reaches_the_node()
    {
        var chain = new FakeChain();
        var svc = new ChainReaderService(chain);
        await Assert.ThrowsAsync<InvalidRequestException>(() => svc.GetTransfersAsync(Token, 0, ChainReaderService.MaxLogBlockRange, null, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidRequestException>(() => svc.GetTransfersAsync(Token, 10, 5, null, null, CancellationToken.None));
        Assert.Equal(0, chain.Calls);
    }

    [Fact]
    public async Task A_wallet_address_is_not_accepted_as_a_token()
    {
        var chain = new FakeChain { IsContract = false };
        var svc = new ChainReaderService(chain);
        var ex = await Assert.ThrowsAsync<InvalidRequestException>(() => svc.GetTokenBalanceAsync(Token, Alice, CancellationToken.None));
        Assert.Contains("no contract code", ex.Message);
        await Assert.ThrowsAsync<InvalidRequestException>(() => svc.GetTransfersAsync(Token, 1, 2, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task More_events_than_the_cap_set_the_truncated_flag()
    {
        var logs = Enumerable.Range(0, ChainReaderService.MaxEvents + 5)
            .Select(i => new RawLog(Token, ["0xddf", Topic(Alice), Topic(Bob)], Word(1), 100, "0x" + i, i)).ToList();
        var svc = new ChainReaderService(new FakeChain { Logs = logs });
        var page = await svc.GetTransfersAsync(Token, 100, 100, null, null, CancellationToken.None);
        Assert.True(page.Truncated);
        Assert.Equal(ChainReaderService.MaxEvents, page.Events.Count);
    }

    [Fact]
    public async Task Bad_addresses_never_reach_the_node()
    {
        var chain = new FakeChain();
        var svc = new ChainReaderService(chain);
        await Assert.ThrowsAsync<InvalidRequestException>(() => svc.GetEthBalanceAsync("nope", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidRequestException>(() => svc.GetTokenBalanceAsync(Token, "0x12", CancellationToken.None));
        Assert.Equal(0, chain.Calls);
    }
}
