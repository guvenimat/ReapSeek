'use client'

import { useRouter } from 'next/navigation'
import { useState, type FormEvent } from 'react'
import { postJson } from '@/lib/api'

export function ChangePasswordForm() {
  const [status, setStatus] = useState<{ ok: boolean; text: string } | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const formEl = e.currentTarget
    const form = new FormData(formEl)
    setBusy(true)
    setStatus(null)
    try {
      await postJson('/auth/password', { current_password: form.get('current_password'), new_password: form.get('new_password') })
      formEl.reset()
      setStatus({ ok: true, text: 'Your password has been updated. Other devices were signed out.' })
    } catch (err) {
      setStatus({ ok: false, text: (err as Error).message })
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="auth-form" data-testid="password-form">
      <label>
        <span>Current password</span>
        <input name="current_password" type="password" autoComplete="current-password" required />
      </label>
      <label>
        <span>New password</span>
        <input name="new_password" type="password" autoComplete="new-password" required minLength={8} maxLength={1024} />
      </label>
      {status && <p className={status.ok ? 'form-success' : 'form-error'} role="status">{status.text}</p>}
      <button type="submit" className="btn btn-secondary" disabled={busy}>Update password</button>
    </form>
  )
}

export function SignOutButton() {
  const router = useRouter()
  const [busy, setBusy] = useState(false)

  async function signOut() {
    setBusy(true)
    await postJson('/auth/logout').catch(() => null)
    router.replace('/')
    router.refresh()
  }

  return (
    <button type="button" className="btn btn-ghost" onClick={signOut} disabled={busy} data-testid="sign-out">
      Sign out
    </button>
  )
}
