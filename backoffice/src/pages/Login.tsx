import { useState, type FormEvent } from 'react'
import { api, type User } from '../api'
import { LogoMark } from '../components/icons'

export function Login({ onSignedIn }: { onSignedIn: (user: User) => void }) {
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
    <div className="login-page">
      <form className="login-card" onSubmit={submit} data-testid="login-form">
        <div className="login-brand">
          <LogoMark size={36} />
          <div>
            <strong>ReapSeek</strong>
            <span>Back office</span>
          </div>
        </div>
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
        <p className="login-note">Access is limited to ReapSeek staff.</p>
      </form>
    </div>
  )
}
