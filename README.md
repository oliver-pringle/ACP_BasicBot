# BasicBot — ACP 2.0 Boilerplate

Empty-shell starter for ACP 2.0 service agents. Two tiers, one tiny stub offering, runnable end-to-end out of the box. Clone this folder, rename, and replace the stub with your real domain.

## Architecture

```
acp-v2/   (Node 22 / TypeScript)            BasicBot.Api/   (.NET 10 / ASP.NET Minimal API)
@virtuals-protocol/acp-node-v2  ──HTTP──►  ADO.NET + SQLite (basicbot.db, bind-mounted)
```

The TS sidecar speaks the ACP v2 protocol (the SDK is Node-only). The C# API holds business logic and the database.

Reference implementation built in this style: `C:\code_crypto\wallet-profiler\`.

## Prerequisites

- .NET 10 SDK
- Node.js 22+
- Docker / Docker Compose (for production)

## Local development

Two terminals:

```bash
# Terminal 1 — C# API on http://localhost:5000
cd BasicBot.Api
dotnet run
```

```bash
# Terminal 2 — ACP sidecar (watches for TS changes)
cd acp-v2
cp .env.example .env       # then fill in agent credentials
# IMPORTANT: for local dev, set BASICBOT_API_URL=http://localhost:5000 in .env
npm install
npm run dev
```

The SQLite file is created automatically:
- Local dev: `BasicBot.Api/basicbot.db`
- Docker: `./data/basicbot.db` (bind-mounted from the host)

Smoke-test the API directly:
```bash
curl http://localhost:5000/health
curl -X POST http://localhost:5000/echo -H "Content-Type: application/json" -d '{"message":"hi"}'
curl http://localhost:5000/echo/1
curl http://localhost:5000/v1/resources/echoStatus
```

## Provisioning the agent

1. Go to https://app.virtuals.io/acp/agents/, create or upgrade an agent to V2.
2. From the **Signers** tab, copy `walletId` and `signerPrivateKey`.
3. Paste into `acp-v2/.env`:
   ```
   ACP_WALLET_ADDRESS=0x...
   ACP_WALLET_ID=...
   ACP_SIGNER_PRIVATE_KEY=0x...
   ACP_CHAIN=baseSepolia
   ```
4. Register offerings — V2 has no programmatic registration, so:
   ```bash
   cd acp-v2
   npm run print-offerings
   ```
   Copy each printed block into **Offerings → New offering** in the dashboard.
5. Register resources (optional but recommended — Butler-style buyer agents
   call Resources before paying for an offering):
   ```bash
   cd acp-v2
   npm run print-resources
   ```
   Copy each printed block into **Resources → New resource** in the dashboard.
   The boilerplate ships with one example (`echoStatus`); add your own in
   `acp-v2/src/resources.ts` and wire matching handlers in `BasicBot.Api/Program.cs`.

## Production (Linux / Docker)

```bash
git clone <your-repo>
cd BasicBot
cp acp-v2/.env.example acp-v2/.env  # then fill in credentials

# One-time: the SQLite bind-mount must be writable by the .NET 10 `app` user
# (UID 1654). Skip this and the API container will crash with EACCES on first
# write. The acp sidecar runs as the `node` user (UID 1000) and has no shared
# state on the host.
mkdir -p data && sudo chown 1654:1654 data

docker compose up -d --build
docker compose logs -f basicbot-acp
```

The API has no published ports — only the sidecar talks to it on the internal `basicbot` bridge network. SQLite persists to `./data/basicbot.db` on the host.

## Security defaults

The boilerplate is hardened for the recommended deployment shape (private docker bridge, no published API ports, TLS terminated at a reverse proxy). The defaults below are safe in that posture; tighten them before deviating.

| Concern | Default | When to change |
|---|---|---|
| **Auth between sidecar and API** | None — both containers on the private `basicbot` bridge with no published ports. | If you publish `basicbot-api` ports or split the containers across hosts: set `BASICBOT_API_KEY` in your shell env (or `acp-v2/.env`). The API enforces `X-API-Key` on every route except `/health` and `/v1/resources/*` (Resources must stay public — that's their whole point). The sidecar forwards the key. |
| **Request body size** | 256 KB cap on Kestrel; per-route message length capped at 10 000 chars. | Bump only if a real offering needs larger inputs. Mirror any change in `acp-v2/src/offerings/*.ts` so oversize is rejected before hitting the API. |
| **Container user** | API runs as `app` (UID 1654, from `mcr.microsoft.com/dotnet/aspnet:10.0`). Sidecar runs as `node` (UID 1000). | Don't run as root. If your `./data` dir is owned by another UID, either `chown 1654:1654 data` or override with a docker-compose `user:` directive. |
| **HTTPS** | Off inside the bridge. The API binds plain HTTP on port 5000 internally. | Always terminate TLS at a reverse proxy (Caddy / nginx / Traefik) before exposing publicly. Don't add `UseHttpsRedirection()` inside the API — it interferes with internal calls from the sidecar. |
| **`AllowedHosts`** | `localhost` for `dotnet run`; `basicbot-api;localhost` inside docker compose. | If you rename the docker-compose service or publish behind a public hostname, update the `AllowedHosts` env var in `docker-compose.yml` to match. |
| **Secrets** | `.env` file in `acp-v2/`. `.gitignore`d. | Acceptable for single-server deployments. For shared / multi-host or regulated environments, switch to a secret manager (AWS Secrets Manager, GCP Secret Manager, Vault, Doppler, 1Password Connect, etc.) and inject env vars at container start. |
| **Base image pinning** | Major-tag pins (`node:22-slim`, `mcr.microsoft.com/dotnet/aspnet:10.0`). | For reproducible production builds, pin to digests (`@sha256:...`) and bump deliberately on a schedule. Trade-off: digest-pinned images don't pick up CVE patches automatically. |

## Wallet delegation guard (EIP-7702)

The sidecar runs a boot-time delegation check before accepting any hires. The
ACP v2 SDK (`acp-node-v2 ^0.0.6`) only recognises wallets delegated to Alchemy
ModularAccountV2 (`0x69007702764179f14F51cdce752f4f775d74E139`). Privy WaaS
occasionally rotates a wallet to a different impl; when that happens, the next
hire fails inside the SDK with `Expected bigint, got: N` from a HexBigInt
typebox encoder that's been fed the wallet's raw integer nonce.

`acp-v2/src/walletDelegation.ts` makes the sidecar self-defending against this:

- **On every boot:** one `eth_getBytecode` call probes the wallet. If the
  delegation prefix (`0xef0100…`) points at ModularAccountV2, the sidecar
  carries on. If not, it either auto-recovers or refuses to start.
- **Auto-recovery (recommended):** set `DEPLOYER_PRIVATE_KEY` in
  `acp-v2/.env`. The guard signs a fresh 7702 authorization via Privy's
  `signer.signAuthorization` and broadcasts a sponsored type-4 tx from the
  deployer EOA. The deployer pays gas (~0.001 ETH per recovery, rare in
  practice). No on-chain tx when delegation is already correct — idempotent.
- **Without a deployer key:** the guard throws on drift with a recovery
  message pointing at `scripts/provision-7702.ts` for a manual one-shot.

`BASE_RPC_URL` in `acp-v2/.env` overrides the public RPC the probe uses
(defaults to publicnode). Even a free RPC is fine — one call per boot.

The guard is wired into `seller.ts` right after `AcpAgent.create(...)`. Do
not remove it. The pattern is shared with ChainlinkBot, where it was
battle-tested through the 2026-05-11 Base mainnet cutover.

## Cloning this boilerplate for a new bot

1. Copy `BasicBot/` → `MyNewBot/`
2. Find/replace `BasicBot` → `MyNewBot` (case-sensitive) in:
   - The folder name
   - `BasicBot.sln` (and rename to `MyNewBot.sln`)
   - `BasicBot.Api/BasicBot.Api.csproj` (rename file too)
   - C# namespaces (`BasicBot.Api.*`)
   - `acp-v2/package.json` (`name` field)
   - `docker-compose.yml` (service + container names)
3. Provision a new agent on app.virtuals.io, copy creds into `acp-v2/.env`
4. Replace the C# domain code:
   - `BasicBot.Api/Data/EchoRepository.cs`, `Services/EchoService.cs`, `Models/EchoRecord.cs`
   - The `/echo` endpoints in `Program.cs`
   - The `echo_records` table in `Db.cs` (`CREATE TABLE IF NOT EXISTS …`)
5. Replace the TS offering:
   - `acp-v2/src/offerings/echo.ts` → your real offering(s). Every `Offering` carries `slaMinutes` (min 5), `requirementSchema`, `requirementExample`, `deliverableSchema`, and `deliverableExample` — fill all of them. Build the schemas from your C# response model; wire keys are camelCase (ASP.NET Core web defaults); any C# enum that flows straight into the response serialises as an integer unless you explicitly `.ToString()` it.
   - Update `registry.ts` and `pricing.ts`
6. Replace the TS resources (optional — delete the example if your bot won't expose any):
   - `acp-v2/src/resources.ts` → your real resources. Resources are public, free, parameterised endpoints buyer / orchestrator agents (Butler etc.) call BEFORE paying for an offering. The example `echoStatus` shows the pattern: declare metadata here, wire the matching `/v1/resources/<name>` handler in `Program.cs`.
   - The X-API-Key middleware in `Program.cs` already whitelists `/v1/resources/*` so resources stay reachable when auth is on.
7. Run `npm run print-offerings` and register on app.virtuals.io. The output includes the SLA, requirement schema + example, and deliverable schema + example per offering for the marketplace registration form and buyer-facing docs. If you have resources, also run `npm run print-resources` and paste each block into the dashboard's Resources tab.

## Useful companion tooling

[`openclaw-acp`](https://github.com/Virtual-Protocol/openclaw-acp) — Virtuals Protocol CLI for managing agent wallets, browsing the marketplace, and registering offerings. Install globally on your dev machine; not bundled with this boilerplate.

## What's intentionally not in this shell

- Redis / caching layer
- EF Core (using classic ADO.NET)
- PostgreSQL / SQL Server (using SQLite)
- API key auth enabled by default (off-by-default `X-API-Key` middleware ships in `Program.cs`; flip on by setting `BASICBOT_API_KEY` — see Security defaults above)
- URL-stored deliverables (only inline; `// TODO:` in `acp-v2/src/deliverable.ts`)
- Chains beyond Base / Base Sepolia
- Test suites — add per-bot as needed
