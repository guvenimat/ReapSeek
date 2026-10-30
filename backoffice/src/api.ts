export type User = { id: number; email: string; name: string; role: string }

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? (method === 'POST' ? '{}' : undefined) : JSON.stringify(body),
    credentials: 'same-origin',
  })
  if (res.status === 204) return undefined as T
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    const message = res.status === 429 ? 'Too many attempts. Wait a minute and try again.' : data?.error ?? `Request failed (${res.status})`
    throw new ApiError(res.status, message)
  }
  return data as T
}

export const api = {
  me: () => request<{ user: User }>('GET', '/api/v1/backoffice/auth/me').then((r) => r.user),
  login: (email: string, password: string) =>
    request<{ user: User }>('POST', '/api/v1/backoffice/auth/login', { email, password }).then((r) => r.user),
  logout: () => request<void>('POST', '/api/v1/backoffice/auth/logout'),
  health: () => request<{ status: string; database: string }>('GET', '/healthz'),
}
