export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

export async function postJson<T = unknown>(path: string, body?: unknown): Promise<T | null> {
  const res = await fetch(`/api/v1${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? '{}' : JSON.stringify(body),
    credentials: 'same-origin',
  })
  if (res.status === 204) return null
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    const message = res.status === 429 ? 'Too many attempts. Please wait a minute and try again.' : data?.error ?? 'Something went wrong. Please try again.'
    throw new ApiError(res.status, message)
  }
  return data as T
}
