'use client'

import Link from 'next/link'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { track } from '@/lib/analytics'
import { formatPrice } from '@/lib/catalog'
import { api, ApiError } from '@/lib/client'
import type { Cart, SavedAddress } from '@/lib/order-types'

type AddressForm = { full_name: string; phone: string; city: string; district: string; line: string; postal_code: string }

const emptyAddress = (name: string): AddressForm => ({ full_name: name, phone: '', city: '', district: '', line: '', postal_code: '' })

type CouponPreview = { code: string; discount: number; total: number; expires_at: string }
type MyCoupon = { code: string; kind: 'percent' | 'fixed'; value: number; min_subtotal: number; max_discount: number | null; expires_at: string; campaign: string | null }

export function CheckoutForm({ cart: initialCart, addresses, customerName }: { cart: Cart; addresses: SavedAddress[]; customerName: string }) {
  const [cart, setCart] = useState(initialCart)
  const [selected, setSelected] = useState<number | 'new'>(addresses[0]?.id ?? 'new')
  const [form, setForm] = useState<AddressForm>(emptyAddress(customerName))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const tracked = useRef(false)
  const [code, setCode] = useState('')
  const [coupon, setCoupon] = useState<CouponPreview | null>(null)
  const [couponError, setCouponError] = useState<string | null>(null)
  const [myCoupons, setMyCoupons] = useState<MyCoupon[]>([])
  const total = coupon?.total ?? cart.total

  useEffect(() => {
    api<{ coupons: MyCoupon[] }>('GET', '/account/coupons').then((r) => setMyCoupons(r.coupons)).catch(() => undefined)
  }, [])

  async function applyCoupon(value: string) {
    setCouponError(null)
    try {
      const preview = await api<CouponPreview>('GET', `/checkout/coupon?code=${encodeURIComponent(value.trim())}`)
      setCoupon(preview)
      setCode(preview.code)
      track('coupon_applied', { code: preview.code, discount: preview.discount })
    } catch (err) {
      setCoupon(null)
      setCouponError((err as Error).message)
    }
  }

  useEffect(() => {
    if (tracked.current) return
    tracked.current = true
    track('checkout_started', { value: initialCart.total, items: initialCart.item_count, sellers: initialCart.packages.length })
  }, [initialCart])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const saved = addresses.find((a) => a.id === selected)
    const address = saved
      ? { full_name: saved.full_name, phone: saved.phone, city: saved.city, district: saved.district, line: saved.line, postal_code: saved.postal_code }
      : form
    setBusy(true)
    setError(null)
    try {
      const res = await api<{ order_number: string; payment_url: string }>('POST', '/checkout', { address, expected_total: total, coupon_code: coupon?.code ?? null })
      window.location.assign(res.payment_url)
    } catch (err) {
      if (err instanceof ApiError && err.code === 'price_changed') {
        setCart(await api<Cart>('GET', '/cart'))
        if (coupon) await applyCoupon(coupon.code)
      }
      if (err instanceof ApiError && err.code?.startsWith('coupon.')) setCoupon(null)
      setError((err as Error).message)
      setBusy(false)
    }
  }

  const field = (key: keyof AddressForm, label: string, props: Record<string, unknown> = {}) => (
    <label className={key === 'line' ? 'wide' : undefined}>
      <span>{label}</span>
      <input name={key} required value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} {...props} />
    </label>
  )

  return (
    <form className="checkout-layout" onSubmit={submit} data-testid="checkout-form">
      <div className="checkout-main">
        <section className="panel">
          <h2>Delivery address</h2>
          {addresses.length > 0 && (
            <div className="address-options">
              {addresses.map((a) => (
                <label key={a.id} className={`address-option${selected === a.id ? ' selected' : ''}`}>
                  <input type="radio" name="address" checked={selected === a.id} onChange={() => setSelected(a.id)} />
                  <span><strong>{a.full_name}</strong><br />{a.line}, {a.district}/{a.city} {a.postal_code}<br /><span className="muted">{a.phone}</span></span>
                </label>
              ))}
              <label className={`address-option${selected === 'new' ? ' selected' : ''}`}>
                <input type="radio" name="address" checked={selected === 'new'} onChange={() => setSelected('new')} />
                <span>Use a new address</span>
              </label>
            </div>
          )}
          {selected === 'new' && (
            <div className="address-form" data-testid="address-form">
              {field('full_name', 'Full name', { maxLength: 100, autoComplete: 'name' })}
              {field('phone', 'Phone', { maxLength: 20, autoComplete: 'tel', inputMode: 'tel', placeholder: '05xx xxx xx xx' })}
              {field('city', 'City', { maxLength: 60, autoComplete: 'address-level1' })}
              {field('district', 'District', { maxLength: 60, autoComplete: 'address-level2' })}
              {field('line', 'Address', { maxLength: 250, autoComplete: 'street-address' })}
              {field('postal_code', 'Postal code', { maxLength: 5, inputMode: 'numeric', autoComplete: 'postal-code', pattern: '[0-9]{5}' })}
            </div>
          )}
        </section>
        <section className="panel">
          <h2>Items</h2>
          {cart.packages.map((p) => (
            <div key={p.seller_id} className="checkout-package">
              <div className="muted small">Sold by <strong>{p.seller_name}</strong> · Shipping {p.shipping_fee === 0 ? 'free' : formatPrice(p.shipping_fee)}</div>
              {p.lines.map((l) => (
                <div key={l.offer_id} className="checkout-line">
                  {l.image_url && <img src={l.image_url} alt="" width={48} height={60} />}
                  <span>{l.quantity} × {l.product_title}<br /><span className="muted small">{l.variant_label}</span></span>
                  <strong>{formatPrice(l.line_total)}</strong>
                </div>
              ))}
            </div>
          ))}
        </section>
      </div>
      <aside className="order-summary">
        <h2>Payment</h2>
        <dl>
          <div><dt>Items ({cart.item_count})</dt><dd>{formatPrice(cart.subtotal)}</dd></div>
          <div><dt>Shipping</dt><dd>{cart.shipping_total === 0 ? 'Free' : formatPrice(cart.shipping_total)}</dd></div>
          {coupon && <div className="discount" data-testid="checkout-discount"><dt>Discount ({coupon.code})</dt><dd>−{formatPrice(coupon.discount)}</dd></div>}
          <div className="total"><dt>Total</dt><dd data-testid="checkout-total">{formatPrice(total)}</dd></div>
        </dl>
        <div className="coupon-box" data-testid="coupon-box">
          {coupon ? (
            <p className="small">Code <strong>{coupon.code}</strong> applied. <button type="button" className="inline-link" onClick={() => { setCoupon(null); setCode('') }}>Remove</button></p>
          ) : (
            <div className="coupon-row">
              <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="Discount code" aria-label="Discount code" name="coupon" maxLength={40} />
              <button type="button" className="btn btn-secondary" disabled={!code.trim()} onClick={() => void applyCoupon(code)}>Apply</button>
            </div>
          )}
          {couponError && <p className="form-error" role="alert" data-testid="coupon-error">{couponError}</p>}
          {!coupon && myCoupons.length > 0 && (
            <div className="my-coupons">
              <span className="muted small">Your codes:</span>
              {myCoupons.map((c) => (
                <button key={c.code} type="button" className="coupon-chip" onClick={() => void applyCoupon(c.code)} data-code={c.code}>
                  {c.kind === 'percent' ? `${c.value}% off` : `${formatPrice(c.value)} off`}{c.min_subtotal > 0 ? ` over ${formatPrice(c.min_subtotal)}` : ''}
                </button>
              ))}
            </div>
          )}
        </div>
        {error && <p className="form-error" role="alert" data-testid="checkout-error">{error}</p>}
        <button type="submit" className="btn btn-accent btn-block" disabled={busy || !cart.can_checkout} data-testid="pay-button">
          {busy ? 'Redirecting to payment…' : `Pay ${formatPrice(total)}`}
        </button>
        <p className="muted small">You will enter your card on the payment provider&apos;s secure page. ReapSeek never sees your card number.</p>
        <Link href="/cart" className="inline-link small">Back to cart</Link>
      </aside>
    </form>
  )
}
