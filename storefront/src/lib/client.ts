'use client'

export class ApiError extends Error {
  constructor(public status: number, message: string, public code: string | null) {
    super(message)
  }
}

export async function api<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api/v1${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'same-origin',
  })
  if (res.status === 204) return undefined as T
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    const message = res.status === 429 ? 'Too many attempts. Please wait a minute.' : data?.error ?? 'Something went wrong. Please try again.'
    throw new ApiError(res.status, message, data?.code ?? null)
  }
  return data as T
}
