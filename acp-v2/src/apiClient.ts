export interface EchoRecord {
  id: number;
  message: string;
  receivedAt: string;
}

export interface HealthResponse {
  status: string;
  time: string;
}

export interface ApiClient {
  health(): Promise<HealthResponse>;
  echo(req: { message: string }): Promise<EchoRecord>;
}

export interface ApiClientOptions {
  timeoutMs?: number;
  apiKey?: string;
}

export function createApiClient(
  baseUrl: string,
  options: ApiClientOptions = {}
): ApiClient {
  const { timeoutMs = 30_000, apiKey } = options;

  async function request<T>(
    path: string,
    init?: RequestInit & { method?: string }
  ): Promise<T> {
    const ctl = new AbortController();
    const timer = setTimeout(() => ctl.abort(), timeoutMs);
    try {
      const headers: Record<string, string> = {
        "Content-Type": "application/json",
        ...((init?.headers as Record<string, string>) ?? {}),
      };
      if (apiKey) headers["X-API-Key"] = apiKey;

      const res = await fetch(`${baseUrl}${path}`, {
        ...init,
        signal: ctl.signal,
        headers,
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`basicbot-api ${res.status}: ${text}`);
      }
      return (await res.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }

  return {
    health: () => request<HealthResponse>("/health"),
    echo: (req) =>
      request<EchoRecord>("/echo", { method: "POST", body: JSON.stringify(req) }),
  };
}
