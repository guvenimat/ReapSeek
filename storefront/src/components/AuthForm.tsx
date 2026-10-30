'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useState, type FormEvent } from 'react'
import { postJson } from '@/lib/api'

type Mode = 'login' | 'register'

export function AuthForm({ mode, next }: { mode: Mode; next: string }) {
  const router = useRouter()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const body = Object.fromEntries(form.entries())
    setBusy(true)
    setError(null)
    try {
      await postJson(mode === 'login' ? '/auth/login' : '/auth/register', body)
      router.replace(next)
      router.refresh()
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  const isLogin = mode === 'login'
  const otherHref = `${isLogin ? '/register' : '/login'}${next !== '/' ? `?next=${encodeURIComponent(next)}` : ''}`

  return (
    <div className="auth-card">
      <div className="auth-tabs">
        {isLogin ? <span className="active">Sign in</span> : <Link href={otherHref}>Sign in</Link>}
        {isLogin ? <Link href={otherHref}>Create account</Link> : <span className="active">Create account</span>}
      </div>
      <form onSubmit={submit} className="auth-form" data-testid={`${mode}-form`}>
        {!isLogin && (
          <label>
            <span>Full name</span>
            <input name="name" autoComplete="name" required maxLength={100} />
          </label>
        )}
        <label>
          <span>Email</span>
          <input name="email" type="email" autoComplete="email" required maxLength={254} />
        </label>
        <label>
          <span>Password</span>
          <input name="password" type="password" autoComplete={isLogin ? 'current-password' : 'new-password'} required
            minLength={isLogin ? undefined : 8} maxLength={1024} />
          {!isLogin && <small className="hint">At least 8 characters</small>}
        </label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <button type="submit" className="btn btn-primary btn-block" disabled={busy}>
          {busy ? 'Please wait…' : isLogin ? 'Sign in' : 'Create account'}
        </button>
      </form>
      {!isLogin && <p className="auth-legal">By creating an account you agree to the terms of use and the privacy notice.</p>}
    </div>
  )
}
