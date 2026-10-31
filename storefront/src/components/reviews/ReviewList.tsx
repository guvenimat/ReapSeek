'use client'

import { useState } from 'react'
import { api } from '@/lib/client'
import type { ReviewsPage } from '@/lib/review-types'
import { Stars } from './Stars'

const date = (value: string) => new Date(value).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Europe/Istanbul' })

export function ReviewList({ productId, initial }: { productId: number; initial: ReviewsPage | null }) {
  const [sort, setSort] = useState('newest')
  const [pages, setPages] = useState<ReviewsPage[]>(initial ? [initial] : [])
  const [busy, setBusy] = useState(false)
  const last = pages.at(-1)
  const reviews = pages.flatMap((p) => p.reviews)

  async function load(nextSort: string, page: number) {
    setBusy(true)
    try {
      const data = await api<ReviewsPage>('GET', `/catalog/products/${productId}/reviews?sort=${nextSort}&page=${page}`)
      setPages((current) => (page === 1 ? [data] : [...current, data]))
    } finally {
      setBusy(false)
    }
  }

  if (!last || last.total === 0) return null
  return (
    <div className="reviews-list">
      <div className="reviews-toolbar">
        <span className="muted small">{last.total} review{last.total === 1 ? '' : 's'}</span>
        <select value={sort} onChange={(e) => { setSort(e.target.value); void load(e.target.value, 1) }} aria-label="Sort reviews">
          <option value="newest">Newest</option>
          <option value="highest">Highest rated</option>
          <option value="lowest">Lowest rated</option>
        </select>
      </div>
      <ul>
        {reviews.map((r) => (
          <li key={r.id} className="review" data-review={r.id}>
            <div className="review-head"><Stars value={r.rating} /><strong>{r.title}</strong></div>
            <p>{r.body}</p>
            <p className="muted small">
              {r.author} · {date(r.created_at)}{r.variant_label ? ` · ${r.variant_label}` : ''} · <span className="verified">Verified purchase</span>
            </p>
          </li>
        ))}
      </ul>
      {last.page * last.page_size < last.total && (
        <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => void load(sort, last.page + 1)}>Show more reviews</button>
      )}
    </div>
  )
}
