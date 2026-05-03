# BasicBot ACP v2 Sidecar

Node.js sidecar that speaks ACP v2 via `@virtuals-protocol/acp-node-v2`, dispatches offerings, and proxies execution to the C# `BasicBot.Api`.

## Setup

1. Provision a BasicBot agent in https://app.virtuals.io/acp/agents/ (V2).
2. From the Signers tab, copy `walletId` and `signerPrivateKey`.
3. Copy `.env.example` → `.env` and fill in credentials.
4. `npm install`
5. `npm run build` — typecheck.
6. `npm start` — runs the seller against the chain in `ACP_CHAIN`.

## Register offerings

V2 has no programmatic registration. Run:

```
npm run print-offerings
```

Copy each block into app.virtuals.io → BasicBot agent → Offerings → New offering.

## Layout

- `src/seller.ts` — entry point
- `src/offerings/` — offering handlers (one stub: `echo`)
- `src/pricing.ts` — USDC price table
- `src/deliverable.ts` — inline vs URL deliverables (50 KB threshold)
- `src/apiClient.ts` — typed HTTP client for the C# API
