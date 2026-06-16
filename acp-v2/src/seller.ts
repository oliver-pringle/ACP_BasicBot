import { AcpAgent } from "@virtuals-protocol/acp-node-v2";
import type { JobSession, JobRoomEntry } from "@virtuals-protocol/acp-node-v2";
import { loadEnv } from "./env.js";
import { createProvider } from "./provider.js";
import { createApiClient } from "./apiClient.js";
import { route } from "./router.js";
import { priceForAssetToken } from "./pricing.js";
import { toDeliverable } from "./deliverable.js";
import { listOfferings, getOffering } from "./offerings/registry.js";
import { listResources } from "./resources.js";
import { ensureDelegation } from "./walletDelegation.js";
import { getChain } from "./chain.js";

type PendingJob = {
  offeringName: string;
  requirement: Record<string, unknown>;
};

async function main() {
  const env = loadEnv();
  const client = createApiClient(env.apiUrl, { apiKey: env.apiKey });

  console.log(`[seller] chain=${env.chain} wallet=${env.walletAddress}`);
  console.log(`[seller] api=${env.apiUrl}`);
  console.log(`[seller] offerings registered (in code): ${listOfferings().length}`);
  console.log(`[seller] resources registered (in code): ${listResources().length}`);

  const provider = await createProvider(env);
  const agent = await AcpAgent.create({ provider });

  // Guard against EIP-7702 delegation drift. The ACP v2 SDK only recognises
  // wallets delegated to Alchemy ModularAccountV2; any other delegation
  // causes the next hire to fail with `Expected bigint, got: N`. Empirically
  // the drift triggers between PrivyAlchemyEvmProviderAdapter.create() and
  // the first hire, so re-check AFTER agent setup and auto-recover if a
  // DEPLOYER_PRIVATE_KEY sponsor is configured. See acp-v2/src/walletDelegation.ts
  // and user-memory reference_acp_wallet_provisioning.md.
  await ensureDelegation({
    adapter: provider,
    walletAddress: env.walletAddress,
    chain: getChain(env.chain),
    rpcUrl: env.baseRpcUrl,
    deployerPrivateKey: env.deployerPrivateKey,
  });

  // Keyed by session.jobId so state survives across entries without mutating
  // the SDK session object. Cleared on terminal events.
  const pending = new Map<string, PendingJob>();

  agent.on("entry", async (session: JobSession, entry: JobRoomEntry) => {
    try {
      if (entry.kind === "system") {
        switch (entry.event.type) {
          case "job.created":
            console.log(`[seller] job.created jobId=${session.jobId}`);
            return;
          case "job.funded":
            return await handleJobFunded(session);
          case "job.completed":
            console.log(`[seller] job.completed jobId=${session.jobId}`);
            pending.delete(session.jobId);
            return;
          case "job.rejected":
            console.log(`[seller] job.rejected jobId=${session.jobId}`);
            pending.delete(session.jobId);
            return;
          case "job.expired":
            pending.delete(session.jobId);
            return;
          default:
            return;
        }
      }

      if (entry.kind === "message" && entry.contentType === "requirement") {
        await handleRequirement(session, entry);
        return;
      }
    } catch (err) {
      console.error(`[seller] handler error for job ${session.jobId}:`, err);
    }
  });

  async function handleRequirement(session: JobSession, entry: JobRoomEntry) {
    if (entry.kind !== "message") return;

    let requirement: Record<string, unknown>;
    try {
      requirement = JSON.parse(entry.content);
    } catch {
      await session.sendMessage("invalid requirement payload");
      return;
    }

    // Offering name lives on the on-chain job description, not in the message body.
    // The SDK's createJobFromOffering sends the raw requirement payload as the
    // "requirement" message and stores offering.name in AcpJob.description.
    const job = session.job ?? (await session.fetchJob());
    const offeringName = job.description;

    // Refuse jobs with a non-zero evaluator. With a buyer-controlled evaluator,
    // the buyer can take our deliverable and then reject() to deny payment.
    // Insisting on the zero-address evaluator means submission auto-completes
    // on-chain.
    const ZERO_ADDRESS = "0x0000000000000000000000000000000000000000";
    if (job.evaluatorAddress.toLowerCase() !== ZERO_ADDRESS) {
      await session.sendMessage(
        `unsupported: this seller only accepts jobs with evaluatorAddress=${ZERO_ADDRESS}. Got: ${job.evaluatorAddress}`
      );
      return;
    }

    const offering = getOffering(offeringName);
    if (!offering) {
      await session.sendMessage(`unknown offering: ${offeringName}`);
      return;
    }
    const v = offering.validate(requirement);
    if (!v.valid) {
      await session.sendMessage(v.reason ?? "validation failed");
      return;
    }

    const price = await priceForAssetToken(offeringName, requirement, session.chainId);
    await session.setBudget(price);

    pending.set(session.jobId, { offeringName, requirement });
  }

  async function handleJobFunded(session: JobSession) {
    let stash = pending.get(session.jobId);
    // Restart-safe recovery: the in-memory `pending` map is lost across a sidecar
    // restart (deploy / OOM / crash) between the requirement and job.funded events,
    // which would otherwise strand a FUNDED job forever — it is never submitted, so
    // it sits OPEN until it expires with the buyer's escrow locked the whole time.
    // Re-derive from the job itself (the requirement + offering name are on-chain).
    if (!stash) stash = await recoverPendingFromJob(session);
    if (!stash) {
      console.warn(`[seller] job.funded without recoverable requirement, jobId=${session.jobId}`);
      return;
    }
    const outcome = await route(stash.offeringName, stash.requirement, { client });
    if (!outcome.ok) {
      await session.sendMessage(`execution failed: ${outcome.reason}`);
      return;
    }
    const payload = await toDeliverable(session.jobId, outcome.result);
    await session.submit(payload);
    console.log(`[seller] submitted jobId=${session.jobId} offering=${stash.offeringName}`);
  }

  // Reconstruct a PendingJob from the on-chain job when the in-memory stash is gone
  // (the sidecar restarted between the requirement and job.funded). Returns undefined
  // if the job / requirement can't be recovered or doesn't validate, in which case the
  // caller falls through to the same warn+return as before — strictly additive, so a
  // restart can now only do BETTER than the previous dead-end, never worse.
  async function recoverPendingFromJob(session: JobSession): Promise<PendingJob | undefined> {
    try {
      const job = session.job ?? (await session.fetchJob());
      const offeringName = job.description;
      const offering = getOffering(offeringName);
      if (!offering) return undefined;
      const raw = (job as { requirements?: unknown }).requirements;
      const requirement: Record<string, unknown> =
        typeof raw === "string" ? JSON.parse(raw) : ((raw as Record<string, unknown> | undefined) ?? {});
      if (!offering.validate(requirement).valid) return undefined;
      console.log(`[seller] recovered requirement for funded job ${session.jobId} after stash loss (offering=${offeringName})`);
      return { offeringName, requirement };
    } catch (err) {
      console.error(`[seller] requirement recovery failed for job ${session.jobId}:`, err);
      return undefined;
    }
  }

  await agent.start();

  const shutdown = async (signal: string) => {
    console.log(`[seller] ${signal} received, stopping agent`);
    try {
      await agent.stop();
    } finally {
      process.exit(0);
    }
  };
  process.on("SIGINT", () => void shutdown("SIGINT"));
  process.on("SIGTERM", () => void shutdown("SIGTERM"));

  console.log("[seller] running  -  waiting for jobs");
}

main().catch((err) => {
  console.error("[seller] fatal:", err);
  process.exit(1);
});
