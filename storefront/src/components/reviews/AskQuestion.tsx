'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useState, type FormEvent } from 'react'
import { track } from '@/lib/analytics'
import { api, ApiError } from '@/lib/client'

export function AskQuestion({ productId, signedIn, slug }: { productId: number; signedIn: boolean; slug: string }) {
  const router = useRouter()
  const [body, setBody] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [sent, setSent] = useState(false)

  if (!signedIn) return <p className="small"><Link href={`/login?next=/p/${slug}%23questions`} className="inline-link">Sign in</Link> to ask the sellers a question.</p>

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await api('POST', `/catalog/products/${productId}/questions`, { body })
      track('question_asked', { item_id: String(productId) })
      setBody('')
      setSent(true)
      router.refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="ask-form" onSubmit={submit} data-testid="ask-form">
      <input required minLength={10} maxLength={500} placeholder="Ask about size, material, delivery..." value={body}
        onChange={(e) => { setBody(e.target.value); setSent(false) }} name="question" aria-label="Your question" />
      <button type="submit" className="btn btn-secondary" disabled={busy || body.trim().length < 10}>Ask</button>
      {sent && <p className="muted small" role="status">Thanks! The sellers will answer here and we will email you.</p>}
      {error && <p className="form-error" role="alert">{error}</p>}
    </form>
  )
}
