'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useState } from 'react'
import { track } from '@/lib/analytics'
import { api } from '@/lib/client'
import type { StoreOffer } from '@/lib/products'
import { HeartIcon } from '../icons'

export function AddToCart({ productId, offer }: { productId: number; offer: StoreOffer | undefined }) {
  const router = useRouter()
  const [quantity, setQuantity] = useState(1)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)
  const max = Math.min(10, offer?.low_stock ?? 10)

  const add = async () => {
    if (!offer) return
    setBusy(true)
    setMessage(null)
    try {
      await api('POST', '/cart/items', { offer_id: offer.id, quantity })
      track('add_to_cart', { item_id: String(productId), offer_id: offer.id, seller_id: offer.seller_id, price: offer.price, quantity })
      setMessage({ ok: true, text: `Added ${quantity} to your cart.` })
      router.refresh()
    } catch (err) {
      setMessage({ ok: false, text: (err as Error).message })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="add-to-cart" data-testid="add-to-cart">
      <div className="pdp-actions">
        <div className="stepper" aria-label="Quantity">
          <button type="button" onClick={() => setQuantity((q) => Math.max(1, q - 1))} disabled={quantity <= 1} aria-label="Decrease quantity">−</button>
          <span data-testid="quantity">{quantity}</span>
          <button type="button" onClick={() => setQuantity((q) => Math.min(max, q + 1))} disabled={quantity >= max} aria-label="Increase quantity">+</button>
        </div>
        <button type="button" className="btn btn-accent btn-lg" disabled={!offer || busy} onClick={add} data-testid="add-to-cart-button">
          {busy ? 'Adding…' : offer ? 'Add to cart' : 'Out of stock'}
        </button>
        <button type="button" className="btn btn-ghost btn-lg icon-only" disabled aria-label="Save to favorites"><HeartIcon /></button>
      </div>
      {message && (
        <p className={message.ok ? 'form-success' : 'form-error'} role="status" data-testid="add-to-cart-message">
          {message.text} {message.ok && <Link href="/cart" className="inline-link">Go to cart</Link>}
        </p>
      )}
    </div>
  )
}
