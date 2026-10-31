import type { Metadata } from 'next'
import { VerifyEmail } from '@/components/AccountRecovery'

export const metadata: Metadata = { title: 'Confirm your email', robots: { index: false }, referrer: 'no-referrer' }

export default async function VerifyEmailPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const raw = (await searchParams).token
  const token = (Array.isArray(raw) ? raw[0] : raw) ?? ''
  return (
    <div className="container auth-page">
      <h1>Confirm your email</h1>
      <VerifyEmail token={token} />
    </div>
  )
}
