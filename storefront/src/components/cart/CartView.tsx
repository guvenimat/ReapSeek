'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useState } from 'react'
import { track } from '@/lib/analytics'
import { formatPrice } from '@/lib/catalog'
import { api } from '@/lib/client'
import type { Cart, CartLine } from '@/lib/order-types'
import { TruckIcon } from '../icons'

export function CartView({ initial, signedIn }: { initial: Cart; signedIn: boolean }) {
  const router = useRouter()
  const [cart, setCart] = useState(initial)
  const [busy, setBusy] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  const change = async (line: CartLine, quantity: number) => {
    setBusy(line.offer_id)
    setError(null)
    try {
      const next = quantity === 0
        ? await api<Cart>('DELETE', `/cart/items/${line.offer_id}`)
        : await api<Cart>('PATCH', `/cart/items/${line.offer_id}`, { quantity })
      if (quantity < line.quantity) track('remove_from_cart', { item_id: String(line.product_id), offer_id: line.offer_id, quantity: line.quantity - quantity })
      setCart(next)
      router.refresh()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(null)
    }
  }

  if (cart.packages.length === 0) {
    return (
      <div className="empty-state" data-testid="cart-empty">
        <strong>Your cart is empty</strong>
        <span>Browse the categories or search for something you like.</span>
        <Link href="/" className="btn btn-primary">Start shopping</Link>
      </div>
    )
  }

  return (
    <div className="cart-layout" data-testid="cart">
      <div className="cart-packages">
        {cart.warnings.length > 0 && (
          <div className="notice warn" role="alert">{cart.warnings.map((w) => <p key={w}>{w}</p>)}</div>
        )}
        {error && <p className="form-error" role="alert">{error}</p>}
        {cart.packages.map((p) => (
          <section key={p.seller_id} className="cart-package" data-seller={p.seller_id}>
            <header>
              <span>Sold by <strong>{p.seller_name}</strong> <span className="rating">★ {p.seller_rating.toFixed(1)}</span></span>
              <span className="shipping-note">
                <TruckIcon size={16} />
                {p.shipping_fee === 0 ? 'Free shipping' : `Add ${formatPrice(p.free_shipping_remaining)} more for free shipping`}
              </span>
            </header>
            {p.lines.map((l) => (
              <div key={l.offer_id} className={`cart-line ${l.status}`} data-offer={l.offer_id}>
                <Link href={`/p/${l.product_slug}`} className="cart-thumb">
                  {l.image_url && <img src={l.image_url} alt="" width={72} height={90} />}
                </Link>
                <div className="cart-info">
                  <Link href={`/p/${l.product_slug}`} className="cart-title"><strong>{l.brand}</strong> {l.product_title.replace(l.brand, '').trim()}</Link>
                  {l.variant_label && <span className="muted small">{l.variant_label}</span>}
                  {l.message && <span className={`line-message ${l.status}`}>{l.message}</span>}
                </div>
                <div className="stepper small-stepper" aria-label="Quantity">
                  <button type="button" onClick={() => change(l, l.quantity - 1)} disabled={busy === l.offer_id} aria-label="Decrease quantity">−</button>
                  <span data-testid="line-quantity">{l.quantity}</span>
                  <button type="button" onClick={() => change(l, l.quantity + 1)} disabled={busy === l.offer_id || l.quantity >= Math.min(10, l.available)} aria-label="Increase quantity">+</button>
                </div>
                <div className="cart-price">
                  {l.list_price !== null && <span className="list-price">{formatPrice(l.list_price * l.quantity)}</span>}
                  <strong>{formatPrice(l.line_total)}</strong>
                </div>
                <button type="button" className="link-button" onClick={() => change(l, 0)} disabled={busy === l.offer_id}>Remove</button>
              </div>
            ))}
          </section>
        ))}
      </div>
      <aside className="order-summary" data-testid="cart-summary">
        <h2>Order summary</h2>
        <dl>
          <div><dt>Items ({cart.item_count})</dt><dd>{formatPrice(cart.subtotal)}</dd></div>
          <div><dt>Shipping</dt><dd>{cart.shipping_total === 0 ? 'Free' : formatPrice(cart.shipping_total)}</dd></div>
          <div className="total"><dt>Total</dt><dd data-testid="cart-total">{formatPrice(cart.total)}</dd></div>
        </dl>
        {cart.can_checkout ? (
          <Link href={signedIn ? '/checkout' : '/login?next=/checkout'} className="btn btn-accent btn-block" data-testid="checkout-link">
            {signedIn ? 'Proceed to checkout' : 'Sign in to check out'}
          </Link>
        ) : (
          <button type="button" className="btn btn-accent btn-block" disabled>Review your cart to continue</button>
        )}
        <p className="muted small">Shipping is calculated per seller: free over ₺500, otherwise ₺39.90.</p>
      </aside>
    </div>
  )
}
