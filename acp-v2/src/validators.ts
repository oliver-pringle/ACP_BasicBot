export interface ValidationResult {
  valid: boolean;
  reason?: string;
}

export function requireString(value: unknown, name: string): ValidationResult {
  if (typeof value !== "string" || value.trim() === "") {
    return { valid: false, reason: `${name} is required` };
  }
  return { valid: true };
}

export function requireStringLength(
  value: unknown,
  name: string,
  maxLen: number
): ValidationResult {
  const base = requireString(value, name);
  if (!base.valid) return base;
  if ((value as string).length > maxLen) {
    return { valid: false, reason: `${name} exceeds ${maxLen} character limit` };
  }
  return { valid: true };
}

export function requireOneOf(
  value: unknown,
  name: string,
  allowed: readonly string[]
): ValidationResult {
  if (value === undefined || value === null) return { valid: true };
  if (typeof value !== "string" || !allowed.includes(value)) {
    return { valid: false, reason: `${name} must be one of: ${allowed.join(", ")}` };
  }
  return { valid: true };
}
