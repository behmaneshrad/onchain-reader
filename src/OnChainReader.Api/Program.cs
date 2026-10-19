using OnChainReader.Core;
using OnChainReader.Nethereum;

var builder = WebApplication.CreateBuilder(args);

// The RPC endpoint comes from configuration / environment (ETH_RPC_URL) - never from code or from the request. Many providers put an API key in the URL, so it is a secret.
var rpcUrl = builder.Configuration["ETH_RPC_URL"] ?? builder.Configuration["Ethereum:RpcUrl"];
builder.Services.AddSingleton<IChainClient>(_ =>
    new NethereumChainClient(rpcUrl ?? throw new InvalidOperationException("Set ETH_RPC_URL (environment variable or user-secrets) to an Ethereum JSON-RPC endpoint.")));
builder.Services.AddSingleton<ChainReaderService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new BigIntegerJsonConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "OnChainReader", Version = "v1", Description = "Read-only Ethereum data API: ETH and ERC-20 balances, Transfer events. No keys, no signing." }));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// One place that maps domain errors to HTTP: caller mistakes -> 400, node problems -> 502.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (InvalidRequestException e) { await Problem(ctx, StatusCodes.Status400BadRequest, "Invalid request", e.Message); }
    catch (UpstreamException e) { await Problem(ctx, StatusCodes.Status502BadGateway, "Ethereum node error", e.Message); }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/balance/{address}", async (string address, ChainReaderService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetEthBalanceAsync(address, ct)))
   .WithName("GetEthBalance").WithSummary("ETH balance of an address (wei and exact decimal)");

app.MapGet("/erc20/{token}/balance/{holder}", async (string token, string holder, ChainReaderService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetTokenBalanceAsync(token, holder, ct)))
   .WithName("GetTokenBalance").WithSummary("ERC-20 balance of a holder, with symbol and decimals");

app.MapGet("/erc20/{token}/transfers", async (string token, ulong fromBlock, ulong toBlock, string? from, string? to, ChainReaderService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetTransfersAsync(token, fromBlock, toBlock, from, to, ct)))
   .WithName("GetTransfers").WithSummary($"Transfer events of a token between two blocks (max {ChainReaderService.MaxLogBlockRange} blocks, {ChainReaderService.MaxEvents} events); optional from/to filters");

app.Run();

static Task Problem(HttpContext ctx, int status, string title, string detail)
{
    ctx.Response.StatusCode = status;
    return ctx.Response.WriteAsJsonAsync(new { title, status, detail });
}

public partial class Program; // for WebApplicationFactory in the tests
