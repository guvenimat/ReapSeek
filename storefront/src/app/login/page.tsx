import type { Metadata } from 'next'
import { redirect } from 'next/navigation'
import { AuthForm } from '@/components/AuthForm'
import { getCurrentUser, safeNext } from '@/lib/session'

export const metadata: Metadata = { title: 'Sign in', robots: { index: false } }

export default async function LoginPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const next = safeNext((await searchParams).next)
  if (await getCurrentUser()) redirect(next)
  return (
    <div className="container auth-page">
      <h1>Welcome back</h1>
      <p className="muted">Sign in to see your orders, favorites and saved addresses.</p>
      <AuthForm mode="login" next={next} />
    </div>
  )
}
