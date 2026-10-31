'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useEffect, useState, type FormEvent } from 'react'
import { track } from '@/lib/analytics'
import { api, ApiError } from '@/lib/client'
import type { Review } from '@/lib/review-types'

type Mine = { eligible: boolean; review: Review | null }

export function WriteReview({ productId, signedIn, slug }: { productId: number; signedIn: boolean; slug: string }) {
  const router = useRouter()
  const [state, setState] = useState<Mine>()
  const [open, setOpen] = useState(false)
  const [form, setForm] = useState({ rating: 0, title: '', body: '' })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  useEffect(() => {
    if (!signedIn) return
    api<Mine>('GET', `/catalog/products/${productId}/my-review`)
      .then((s) => {
        setState(s)
        if (s.review) setForm({ rating: s.review.rating, title: s.review.title, body: s.review.body })
      })
      .catch(() => setState({ eligible: false, review: null }))
  }, [productId, signedIn])

  if (!signedIn) return <p className="small"><Link href={`/login?next=/p/${slug}%23reviews`} className="inline-link">Sign in</Link> to review a product you bought.</p>
  if (!state) return null
  if (!state.eligible) return <p className="muted small">You can review this product once you have received it.</p>
  if (!open) {
    return (
      <button type="button" className="btn btn-secondary" onClick={() => setOpen(true)} data-testid="write-review">
        {state.review ? 'Edit your review' : 'Write a review'}
      </button>
    )
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await api('PUT', `/catalog/products/${productId}/review`, form)
      track('review_submitted', { item_id: String(productId), rating: form.rating, edited: state?.review != null })
      setOpen(false)
      router.refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="review-form" onSubmit={submit} data-testid="review-form">
      <div className="star-input" role="radiogroup" aria-label="Your rating">
        {[1, 2, 3, 4, 5].map((n) => (
          <button key={n} type="button" role="radio" aria-checked={form.rating === n} aria-label={`${n} star${n === 1 ? '' : 's'}`}
            className={n <= form.rating ? 'on' : ''} onClick={() => setForm({ ...form, rating: n })} data-star={n}>★</button>
        ))}
      </div>
      <input required minLength={3} maxLength={100} placeholder="Sum it up in a few words" value={form.title}
        onChange={(e) => setForm({ ...form, title: e.target.value })} name="title" aria-label="Review title" />
      <textarea required minLength={10} maxLength={2000} rows={4} placeholder="What did you like or dislike? How are the fit and quality?" value={form.body}
        onChange={(e) => setForm({ ...form, body: e.target.value })} name="body" aria-label="Review" />
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="return-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || form.rating === 0}>{state.review ? 'Update review' : 'Post review'}</button>
        <button type="button" className="btn btn-ghost" onClick={() => setOpen(false)}>Cancel</button>
      </div>
    </form>
  )
}
