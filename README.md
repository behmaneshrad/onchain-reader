# OnChainReader

A small, **read-only Ethereum data API** in ASP.NET Core 8: ETH and ERC-20 balances and ERC-20 `Transfer` events from any JSON-RPC node. It never holds a key, never signs and never sends a transaction.

Built as a compact example of how I write blockchain-facing back ends: exact arithmetic, strict input validation, bounded queries, a clean seam for testing, and honest error mapping.

## Endpoints
| Method | Route | Returns |
|---|---|---|
| GET | `/health` | `{ "status": "ok" }` |
| GET | `/balance/{address}` | ETH balance: `wei` (string), `eth` (exact decimal string), `blockNumber` |
| GET | `/erc20/{token}/balance/{holder}` | token `symbol`, `decimals`, `raw`, `formatted`, `blockNumber` |
| GET | `/erc20/{token}/transfers?fromBlock=&toBlock=&from=&to=` | `{ events: [...], truncated }` — `Transfer` events with exact `formattedValue` |

Errors: **400** for caller mistakes (bad address, wrong checksum, block range too large, "not an ERC-20 token"), **502** when the node cannot answer. Swagger UI is available in Development.

## Design decisions
- **Exact amounts.** Balances are `BigInteger`, formatted to decimal strings by integer arithmetic — never `double`/`decimal`. Large numbers are serialised as **strings** (JSON numbers lose precision above 2^53 in JavaScript).
- **Strict addresses.** `0x` + 40 hex; a mixed-case address must carry a valid **EIP-55 checksum**, so a typo in a checksummed address is rejected instead of silently querying another account.
- **"Is this really a token?"** A plain wallet answers every ERC-20 call with empty data, which naive code reads as `decimals = 0, balance = 0`. The service checks `eth_getCode` first. EIP-7702 delegated wallets (`0xef0100…` code, e.g. vitalik.eth on mainnet) are treated as wallets.
- **Bounded queries.** At most 2 000 blocks and 1 000 events per request; if more matched, the response says `truncated: true`.
- **Tolerant log parsing.** ERC-721 `Transfer` logs (4 topics) and malformed logs are skipped; one odd contract cannot break a response.
- **Symbols.** Handles both `string` and legacy `bytes32` (MKR-style) `symbol()` returns; a missing symbol is `null`, not an error.
- **Seam for tests.** `IChainClient` is the only thing the logic knows about the network; the Nethereum adapter implements it and tests use an in-memory fake.
- **No secrets in code.** The RPC URL (often contains an API key) comes from `ETH_RPC_URL` / user-secrets.

## Run
```bash
export ETH_RPC_URL="https://ethereum-rpc.publicnode.com"   # any Ethereum JSON-RPC endpoint (read-only calls only)
dotnet run --project src/OnChainReader.Api
```
Or with Docker:
```bash
docker build -t onchain-reader .
docker run -p 8080:8080 -e ETH_RPC_URL=https://ethereum-rpc.publicnode.com onchain-reader
```
Try: `GET /erc20/0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48/balance/0x28C6c06298d514Db089934071355E5743bf21d60`

## Tests
```bash
dotnet test
```
55 tests, all offline: amount formatting (incl. values above double precision), hex/topic/symbol decoding, address and checksum validation, contract detection, service logic with a fake node (filtering, ordering, range limits, truncation, malformed logs), and HTTP behaviour through `WebApplicationFactory` (status codes, problem bodies, string-encoded big numbers).

## Verified against a live node
Run against a public mainnet RPC (read-only): ETH balance, USDC `symbol`/`decimals`/`balanceOf`, a 20-block `Transfer` query (1 000-event cap and `truncated` flag), `from`/`to` filters, rejection of a bad checksum, and rejection of wallet addresses passed as tokens. Two bugs were found this way (BigInteger JSON serialisation, wallet-as-token) and fixed with tests.

## Not included (on purpose)
No writes, no signing, no authentication, no caching/rate limiting, a single chain per instance. For production put it behind an API gateway and add caching.

## Project layout
```
src/OnChainReader.Core        models, amount/address logic, ChainReaderService, IChainClient
src/OnChainReader.Nethereum   IChainClient over Nethereum (JSON-RPC)
src/OnChainReader.Api         minimal API, error mapping
tests/OnChainReader.Tests     unit + HTTP tests, FakeChain
```

MIT licensed.
