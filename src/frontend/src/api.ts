export class ApiError extends Error {
  readonly status: number;

  constructor(
    message: string,
    status: number,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

let csrfToken: string | null = null;

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken;

  const response = await fetch("/api/v1/auth/csrf", {
    credentials: "include",
  });
  if (!response.ok) {
    throw new ApiError("Could not establish a secure request token.", response.status);
  }

  const result = (await response.json()) as { requestToken: string };
  csrfToken = result.requestToken;
  return csrfToken;
}

export async function api<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const method = (init.method ?? "GET").toUpperCase();
  const headers = new Headers(init.headers);
  if (init.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (!["GET", "HEAD", "OPTIONS"].includes(method)) {
    headers.set("X-ERP-CSRF", await getCsrfToken());
  }

  const response = await fetch(path, {
    ...init,
    method,
    headers,
    credentials: "include",
  });
  if (!response.ok) {
    const body = (await response.json().catch(() => null)) as
      | { title?: string; error?: string; errors?: Record<string, string[]> }
      | null;
    const validation = body?.errors
      ? Object.values(body.errors).flat().join(" ")
      : undefined;
    throw new ApiError(
      validation || body?.error || body?.title || `Request failed (${response.status}).`,
      response.status,
    );
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface RecordData {
  id: string;
  code?: string;
  name?: string;
  isActive?: boolean;
  [key: string]: unknown;
}

export interface SelectOption {
  id: string;
  label: string;
}
