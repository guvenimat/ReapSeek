import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import { api, type User } from '../api'
import { LogoMark } from '../components/icons'
import { navigate, useRoute } from '../lib'

export function Login({ onSignedIn }: { onSignedIn: (user: User) => void }) {
  const route = useRoute()
  const [page] = route.path
  if (page === 'forgot-password') return <ForgotPassword />
  if (page === 'reset-password') return <ResetPassword token={route.query.get('token') ?? ''} />
  return <SignIn onSignedIn={onSignedIn} />
}

function Frame({ children, onSubmit, testId }: { children: ReactNode; onSubmit?: (e: FormEvent<HTMLFormElement>) => void; testId: string }) {
  return (
    <div className="login-page">
      <form className="login-card" onSubmit={onSubmit ?? ((e) => e.preventDefault())} data-testid={testId}>
        <div className="login-brand">
          <LogoMark size={36} />
          <div>
            <strong>ReapSeek</strong>
            <span>Back office</span>
          </div>
        </div>
        {children}
      </form>
    </div>
  )
}

function SignIn({ onSignedIn }: { onSignedIn: (user: User) => void }) {
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    setBusy(true)
    setError(null)
    try {
      onSignedIn(await api.login(String(form.get('email')), String(form.get('password'))))
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  return (
    <Frame onSubmit={submit} testId="login-form">
      <label>
        <span>Email</span>
        <input name="email" type="email" autoComplete="username" required />
      </label>
      <label>
        <span>Password</span>
        <input name="password" type="password" autoComplete="current-password" required />
      </label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      <p className="login-note">
        Access is limited to ReapSeek staff and sellers. <a href="#/forgot-password" data-testid="forgot-link">Forgot your password?</a>
      </p>
    </Frame>
  )
}

function ForgotPassword() {
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.forgotPassword(String(new FormData(e.currentTarget).get('email')))
      setSent(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (sent) {
    return (
      <Frame testId="forgot-sent">
        <p>If a seller account exists for this address, we sent a link to choose a new password. It works once, for 30 minutes.</p>
        <button type="button" className="btn btn-ghost" onClick={() => navigate('')}>Back to sign in</button>
      </Frame>
    )
  }
  return (
    <Frame onSubmit={submit} testId="forgot-form">
      <p className="muted small">Sellers can reset their password here. Staff accounts are managed by the platform team.</p>
      <label>
        <span>Email</span>
        <input name="email" type="email" autoComplete="username" required />
      </label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Please wait…' : 'Send reset link'}</button>
      <p className="login-note"><a href="#/">Back to sign in</a></p>
    </Frame>
  )
}

function ResetPassword({ token }: { token: string }) {
  const [valid, setValid] = useState<boolean>()
  const [done, setDone] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.checkResetToken(token).then(setValid, () => setValid(false))
  }, [token])

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const password = String(form.get('password'))
    if (password !== String(form.get('confirm'))) {
      setError('The two passwords do not match.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await api.resetPassword(token, password)
      setDone(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  if (valid === undefined) return <div className="boot" aria-busy="true" />
  if (done || !valid) {
    return (
      <Frame testId={done ? 'reset-done' : 'reset-invalid'}>
        <p>{done ? 'Your password is changed and you were signed out everywhere. Sign in with your new password.' : 'This link is invalid or has expired. Reset links work once, for 30 minutes.'}</p>
        <button type="button" className="btn btn-primary" onClick={() => navigate(done ? '' : 'forgot-password')}>{done ? 'Sign in' : 'Ask for a new link'}</button>
      </Frame>
    )
  }
  return (
    <Frame onSubmit={submit} testId="reset-form">
      <label>
        <span>New password</span>
        <input name="password" type="password" autoComplete="new-password" required minLength={8} maxLength={1024} />
      </label>
      <label>
        <span>Repeat the new password</span>
        <input name="confirm" type="password" autoComplete="new-password" required minLength={8} maxLength={1024} />
      </label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <button type="submit" className="btn btn-primary" disabled={busy}>{busy ? 'Please wait…' : 'Save new password'}</button>
    </Frame>
  )
}
