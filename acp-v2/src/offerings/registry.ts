import type { Offering } from "./types.js";
import { echo } from "./echo.js";

export const OFFERINGS: Record<string, Offering> = {
  echo,
};

export function getOffering(name: string): Offering | undefined {
  return OFFERINGS[name];
}

export function listOfferings(): string[] {
  return Object.keys(OFFERINGS);
}
