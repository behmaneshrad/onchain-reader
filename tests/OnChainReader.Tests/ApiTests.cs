using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnChainReader.Core;

namespace OnChainReader.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Token = "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48";
    private const string Alice = "0x1111111111111111111111111111111111111111";

    private readonly FakeChain _chain = new() { BlockNumber = 123, EthBalance = BigInteger.Parse("1500000000000000000"), Decimals = 6, Symbol = "USDC", TokenBalance = 2_500_000 };
    private readonly HttpClient _http;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _http = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IChainClient>();
            s.AddSingleton<IChainClient>(_chain);
        })).CreateClient();
    }

    [Fact]
    public async Task Health_is_ok()
    {
        var r = await _http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Balance_returns_exact_json()
    {
        var r = await _http.GetAsync($"/balance/{Alice}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("1.5", doc.RootElement.GetProperty("eth").GetString());
        Assert.Equal(123, doc.RootElement.GetProperty("blockNumber").GetInt32());
    }

    [Fact]
    public async Task Big_numbers_are_serialised_as_decimal_strings()
    {
        _chain.EthBalance = BigInteger.Parse("123456789012345678901234567890");
        var r = await _http.GetFromJsonAsync<JsonElement>($"/balance/{Alice}");
        Assert.Equal(JsonValueKind.String, r.GetProperty("wei").ValueKind);
        Assert.Equal("123456789012345678901234567890", r.GetProperty("wei").GetString());
    }

    [Fact]
    public async Task Token_balance_returns_symbol_and_formatted_amount()
    {
        var r = await _http.GetAsync($"/erc20/{Token}/balance/{Alice}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("USDC", doc.RootElement.GetProperty("symbol").GetString());
        Assert.Equal("2.5", doc.RootElement.GetProperty("formatted").GetString());
    }

    [Fact]
    public async Task A_bad_address_is_a_400_with_a_problem_body()
    {
        var r = await _http.GetAsync("/balance/not-an-address");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("0x", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Transfers_need_a_bounded_range()
    {
        var r = await _http.GetAsync($"/erc20/{Token}/transfers?fromBlock=0&toBlock=5000000");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Transfers_return_events()
    {
        _chain.Logs.Add(new RawLog(Token,
            ["0xddf", "0x000000000000000000000000" + Alice[2..], "0x000000000000000000000000" + "2222222222222222222222222222222222222222"],
            "0x" + new BigInteger(1_000_000).ToString("x").PadLeft(64, '0'), 100, "0xaaa", 0));
        var r = await _http.GetFromJsonAsync<JsonElement>($"/erc20/{Token}/transfers?fromBlock=90&toBlock=110&from={Alice}");
        Assert.False(r.GetProperty("truncated").GetBoolean());
        var events = r.GetProperty("events");
        Assert.Equal(1, events.GetArrayLength());
        Assert.Equal("1", events[0].GetProperty("formattedValue").GetString());
    }

    [Fact]
    public async Task An_unreachable_node_is_a_502_not_a_500()
    {
        _chain.Fail = new UpstreamException("timeout");
        var r = await _http.GetAsync($"/balance/{Alice}");
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
    }
}
