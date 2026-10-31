'use client'

import { useRouter } from 'next/navigation'
import { useState, type FormEvent } from 'react'
import { formatPrice } from '@/lib/catalog'
import { api, ApiError } from '@/lib/client'
import { returnReasons, type OrderPackage } from '@/lib/order-types'

export function ReturnForm({ orderNumber, pkg, deadline }: { orderNumber: string; pkg: OrderPackage; deadline: string }) {
  const router = useRouter()
  const [open, setOpen] = useState(false)
  const [quantities, setQuantities] = useState<Record<string, number>>({})
  const [reason, setReason] = useState('')
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const lines = pkg.lines.filter((l) => l.returnable_quantity > 0)
  const chosen = lines.filter((l) => (quantities[l.id] ?? 0) > 0)
  const refund = chosen.reduce((sum, l) => sum + l.unit_price * quantities[l.id], 0)

  if (!open) {
    return (
      <div className="return-cta">
        <button type="button" className="btn btn-ghost" onClick={() => setOpen(true)} data-testid="start-return">Return items</button>
        <span className="muted small">Free returns until {deadline}</span>
      </div>
    )
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await api('POST', `/orders/${encodeURIComponent(orderNumber)}/returns`, {
        package_id: pkg.id, reason, comment, items: chosen.map((l) => ({ order_line_id: l.id, quantity: quantities[l.id] })),
      })
      setOpen(false)
      setQuantities({})
      router.refresh()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Something went wrong. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="return-form" onSubmit={submit} data-testid="return-form">
      <strong>Which items are you returning?</strong>
      {lines.map((l) => (
        <label key={l.id} className="return-line">
          <span>{l.product_title}<br /><span className="muted small">{l.variant_label} · {formatPrice(l.unit_price)} each</span></span>
          <select value={quantities[l.id] ?? 0} onChange={(e) => setQuantities({ ...quantities, [l.id]: Number(e.target.value) })} aria-label={`Quantity of ${l.product_title} to return`}>
            {Array.from({ length: l.returnable_quantity + 1 }, (_, n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </label>
      ))}
      <label className="field">
        <span>Reason</span>
        <select required value={reason} onChange={(e) => setReason(e.target.value)} name="reason">
          <option value="" disabled>Choose a reason</option>
          {Object.entries(returnReasons).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select>
      </label>
      <label className="field">
        <span>Anything the seller should know? (optional)</span>
        <textarea rows={2} maxLength={500} value={comment} onChange={(e) => setComment(e.target.value)} name="comment" />
      </label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="return-actions">
        <button type="submit" className="btn btn-primary" disabled={busy || chosen.length === 0 || !reason}>
          {chosen.length === 0 ? 'Choose items' : `Request a return · ${formatPrice(refund)}`}
        </button>
        <button type="button" className="btn btn-ghost" onClick={() => setOpen(false)}>Cancel</button>
      </div>
      <p className="muted small">The seller reviews your request. Once approved, you get a return code for Sandbox Cargo and we refund your card when the parcel arrives.</p>
    </form>
  )
}
