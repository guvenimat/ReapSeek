'use client'

import Link from 'next/link'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { postJson } from '@/lib/api'

export function ForgotPasswordForm() {
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const email = String(new FormData(e.currentTarget).get('email') ?? '')
    setBusy(true)
    setError(null)
    try {
      await postJson('/auth/password/forgot', { email })
      setSent(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (sent) {
    return (
      <div className="auth-card" data-testid="forgot-sent">
        <h2>Check your inbox</h2>
        <p className="muted">If an account exists for this address, we sent a link to choose a new password. It works once, for 30 minutes.</p>
        <Link href="/login" className="btn btn-ghost btn-block">Back to sign in</Link>
      </div>
    )
  }
  return (
    <div className="auth-card">
      <form onSubmit={submit} className="auth-form" data-testid="forgot-form">
        <label>
          <span>Email</span>
          <input name="email" type="email" autoComplete="email" required maxLength={254} />
        </label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <button type="submit" className="btn btn-primary btn-block" disabled={busy}>{busy ? 'Please wait…' : 'Send reset link'}</button>
      </form>
      <p className="auth-legal"><Link href="/login">Back to sign in</Link></p>
    </div>
  )
}

export function ResetPasswordForm({ token }: { token: string }) {
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const password = String(form.get('password') ?? '')
    if (password !== String(form.get('confirm') ?? '')) {
      setError('The two passwords do not match.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await postJson('/auth/password/reset', { token, password })
      setDone(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (done) {
    return (
      <div className="auth-card" data-testid="reset-done">
        <h2>Your password is changed</h2>
        <p className="muted">You were signed out on every device. Sign in with your new password.</p>
        <Link href="/login" className="btn btn-primary btn-block">Sign in</Link>
      </div>
    )
  }
  return (
    <div className="auth-card">
      <form onSubmit={submit} className="auth-form" data-testid="reset-form">
        <label>
          <span>New password</span>
          <input name="password" type="password" autoComplete="new-password" required minLength={8} maxLength={1024} />
          <small className="hint">At least 8 characters</small>
        </label>
        <label>
          <span>Repeat the new password</span>
          <input name="confirm" type="password" autoComplete="new-password" required minLength={8} maxLength={1024} />
        </label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <button type="submit" className="btn btn-primary btn-block" disabled={busy}>{busy ? 'Please wait…' : 'Save new password'}</button>
      </form>
    </div>
  )
}

export function VerifyEmail({ token }: { token: string }) {
  const [state, setState] = useState<'working' | 'done' | 'failed'>('working')
  const [error, setError] = useState<string | null>(null)
  const started = useRef(false)

  useEffect(() => {
    if (started.current) return
    started.current = true
    postJson('/auth/email/verify', { token }).then(() => setState('done'), (err: Error) => {
      setError(err.message)
      setState('failed')
    })
  }, [token])

  return (
    <div className="auth-card" data-testid={`verify-${state}`}>
      {state === 'working' && <p className="muted">Confirming your email address…</p>}
      {state === 'done' && (
        <>
          <h2>Your email address is confirmed</h2>
          <p className="muted">Thanks! We will use it for your orders and offers.</p>
          <Link href="/" className="btn btn-primary btn-block">Continue shopping</Link>
        </>
      )}
      {state === 'failed' && (
        <>
          <h2>We could not confirm your email</h2>
          <p className="muted">{error} You can ask for a new link from your account page.</p>
          <Link href="/account" className="btn btn-ghost btn-block">Go to my account</Link>
        </>
      )}
    </div>
  )
}

export function VerifyEmailNotice() {
  const [state, setState] = useState<'idle' | 'busy' | 'sent'>('idle')
  const [error, setError] = useState<string | null>(null)

  async function resend() {
    setState('busy')
    setError(null)
    try {
      await postJson('/auth/email/verification')
      setState('sent')
    } catch (err) {
      setError((err as Error).message)
      setState('idle')
    }
  }

  return (
    <div className="notice warn verify-notice" data-testid="verify-notice">
      <p><strong>Please confirm your email address.</strong> We sent you a link when you signed up.</p>
      {state === 'sent'
        ? <p data-testid="verify-resent">A new link is on its way. It works for 3 days.</p>
        : <p><button type="button" className="link-button" onClick={resend} disabled={state === 'busy'}>Send me a new link</button></p>}
      {error && <p className="form-error" role="alert">{error}</p>}
    </div>
  )
}
