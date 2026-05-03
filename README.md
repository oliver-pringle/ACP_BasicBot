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
| **Auth between sidecar and API** | None — both containers on the private `basicbot` bridge with no published ports. | If you publish `basicbot-api` ports or split the containers across hosts: set `BASICBOT_API_KEY` in your shell env (or `acp-v2/.env`). The API enforces `X-API-Key` on every non-`/health` route, the sidecar forwards it. |
| **Request body size** | 256 KB cap on Kestrel; per-route message length capped at 10 000 chars. | Bump only if a real offering needs larger inputs. Mirror any change in `acp-v2/src/offerings/*.ts` so oversize is rejected before hitting the API. |
| **Container user** | API runs as `app` (UID 1654, from `mcr.microsoft.com/dotnet/aspnet:10.0`). Sidecar runs as `node` (UID 1000). | Don't run as root. If your `./data` dir is owned by another UID, either `chown 1654:1654 data` or override with a docker-compose `user:` directive. |
| **HTTPS** | Off inside the bridge. The API binds plain HTTP on port 5000 internally. | Always terminate TLS at a reverse proxy (Caddy / nginx / Traefik) before exposing publicly. Don't add `UseHttpsRedirection()` inside the API — it interferes with internal calls from the sidecar. |
| **`AllowedHosts`** | `localhost` for `dotnet run`; `basicbot-api;localhost` inside docker compose. | If you rename the docker-compose service or publish behind a public hostname, update the `AllowedHosts` env var in `docker-compose.yml` to match. |
| **Secrets** | `.env` file in `acp-v2/`. `.gitignore`d. | Acceptable for single-server deployments. For shared / multi-host or regulated environments, switch to a secret manager (AWS Secrets Manager, GCP Secret Manager, Vault, Doppler, 1Password Connect, etc.) and inject env vars at container start. |
| **Base image pinning** | Major-tag pins (`node:22-slim`, `mcr.microsoft.com/dotnet/aspnet:10.0`). | For reproducible production builds, pin to digests (`@sha256:...`) and bump deliberately on a schedule. Trade-off: digest-pinned images don't pick up CVE patches automatically. |

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
   - `acp-v2/src/offerings/echo.ts` → your real offering(s)
   - Update `registry.ts` and `pricing.ts`
6. Run `npm run print-offerings` and register on app.virtuals.io.

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
