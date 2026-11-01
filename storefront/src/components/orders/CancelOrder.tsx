'use client'

import { useRouter } from 'next/navigation'
import { useState } from 'react'
import { api, ApiError } from '@/lib/client'

export function CancelOrder({ orderNumber, paid, total }: { orderNumber: string; paid: boolean; total: string }) {
  const router = useRouter()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()

  async function cancel() {
    setBusy(true)
    setError(undefined)
    try {
      await api('POST', `/orders/${encodeURIComponent(orderNumber)}/cancel`)
      router.refresh()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong. Please try again.')
      setBusy(false)
    }
  }

  return (
    <div className="notice cancel-order" data-testid="cancel-order">
      {confirming ? (
        <>
          <span>{paid ? `Cancel this order? We refund ${total} to your card and the sellers will not ship it.` : 'Cancel this order? Nothing will be charged.'}</span>
          <span className="cancel-actions">
            <button type="button" className="btn btn-sm btn-danger" onClick={cancel} disabled={busy} data-testid="confirm-cancel">{busy ? 'Cancelling…' : 'Yes, cancel'}</button>
            <button type="button" className="btn btn-sm btn-ghost" onClick={() => setConfirming(false)} disabled={busy}>Keep it</button>
          </span>
        </>
      ) : (
        <>
          <span>{paid ? 'Changed your mind? You can cancel until a seller ships your package.' : 'You can cancel this order before paying.'}</span>
          <button type="button" className="btn btn-sm btn-ghost" onClick={() => setConfirming(true)} data-testid="start-cancel">Cancel order</button>
        </>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}
    </div>
  )
}
