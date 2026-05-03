import type { Offering } from "./types.js";
import { requireStringLength } from "../validators.js";

const MAX_MESSAGE_LENGTH = 10_000;

export const echo: Offering = {
  name: "echo",
  description:
    "Echo a message back. Demonstrates the BasicBot ACP boilerplate end-to-end (validate → price → call C# API → SQLite write → deliverable).",
  requirementSchema: {
    type: "object",
    properties: {
      message: {
        type: "string",
        description: "The message to echo back.",
        maxLength: MAX_MESSAGE_LENGTH,
      },
    },
    required: ["message"],
  },
  validate(req) {
    return requireStringLength(req.message, "message", MAX_MESSAGE_LENGTH);
  },
  async execute(req, { client }) {
    return await client.echo({ message: String(req.message) });
  },
};
