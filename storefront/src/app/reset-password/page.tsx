import type { Metadata } from 'next'
import Link from 'next/link'
import { ResetPasswordForm } from '@/components/AccountRecovery'
import { checkResetToken } from '@/lib/session'

export const metadata: Metadata = { title: 'Choose a new password', robots: { index: false }, referrer: 'no-referrer' }

export default async function ResetPasswordPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const raw = (await searchParams).token
  const token = Array.isArray(raw) ? raw[0] : raw
  const valid = token ? await checkResetToken(token) : false
  return (
    <div className="container auth-page">
      <h1>Choose a new password</h1>
      {valid && token ? (
        <ResetPasswordForm token={token} />
      ) : (
        <div className="auth-card" data-testid="reset-invalid">
          <p className="muted">This link is invalid or has expired. Reset links work once, for 30 minutes.</p>
          <Link href="/forgot-password" className="btn btn-primary btn-block">Ask for a new link</Link>
        </div>
      )}
    </div>
  )
}
