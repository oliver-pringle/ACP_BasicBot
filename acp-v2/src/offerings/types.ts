import type { ValidationResult } from "../validators.js";
import type { ApiClient } from "../apiClient.js";

export interface OfferingContext {
  client: ApiClient;
}

export interface Offering {
  name: string;
  description: string;
  // Required: estimated maximum job duration in minutes (min 5). Buyer-facing
  // SLA — the marketplace shows this so buyers know the wall-clock window
  // between hire and deliverable.
  slaMinutes: number;
  requirementSchema: Record<string, unknown>;
  // Required: realistic example payload that satisfies requirementSchema.
  // Goes into the marketplace registration form so buyers see both the
  // request shape AND the deliverable shape before hiring.
  requirementExample: unknown;
  // Required: deliverable contract (JSON Schema) + one realistic example. Build the
  // schema from the C# response model — ASP.NET Core's web defaults emit camelCase
  // keys but DO NOT register JsonStringEnumConverter, so any C# enum that flows
  // into the response without an explicit .ToString() serialises as an integer.
  deliverableSchema: Record<string, unknown>;
  deliverableExample: unknown;
  validate(req: Record<string, unknown>): ValidationResult;
  execute(req: Record<string, unknown>, ctx: OfferingContext): Promise<unknown>;
}
