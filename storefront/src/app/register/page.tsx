import type { Metadata } from 'next'
import { redirect } from 'next/navigation'
import { AuthForm } from '@/components/AuthForm'
import { getCurrentUser, safeNext } from '@/lib/session'

export const metadata: Metadata = { title: 'Create account', robots: { index: false } }

export default async function RegisterPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const next = safeNext((await searchParams).next)
  if (await getCurrentUser()) redirect(next)
  return (
    <div className="container auth-page">
      <h1>Create your account</h1>
      <p className="muted">Track orders, save favorites and check out faster.</p>
      <AuthForm mode="register" next={next} />
    </div>
  )
}
