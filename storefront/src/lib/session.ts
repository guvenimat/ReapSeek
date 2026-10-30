import { cookies } from 'next/headers'

export type User = { id: number; email: string; name: string; role: string }

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'
const sessionCookie = 'rs_session'

export async function getCurrentUser(): Promise<User | null> {
  const token = (await cookies()).get(sessionCookie)?.value
  if (!token) return null
  try {
    const res = await fetch(`${apiUrl}/api/v1/auth/me`, {
      headers: { cookie: `${sessionCookie}=${token}` },
      cache: 'no-store',
    })
    if (!res.ok) return null
    return ((await res.json()) as { user: User }).user
  } catch {
    return null
  }
}

export function safeNext(value: string | string[] | undefined, fallback = '/'): string {
  const next = Array.isArray(value) ? value[0] : value
  return next && next.startsWith('/') && !next.startsWith('//') && !next.startsWith('/\\') ? next : fallback
}
