'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useEffect, useState } from 'react'
import { formatPrice } from '@/lib/catalog'
import { api } from '@/lib/client'
import { failureLabel, type OrderDetail } from '@/lib/order-types'

const pollEveryMs = 1500
const giveUpAfterMs = 30_000

export function PaymentResult({ initial }: { initial: OrderDetail }) {
  const router = useRouter()
  const [order, setOrder] = useState(initial)
  const [waitedTooLong, setWaitedTooLong] = useState(false)

  useEffect(() => {
    if (order.status !== 'pending_payment') return
    const started = Date.now()
    const timer = setInterval(async () => {
      const next = await api<OrderDetail>('GET', `/orders/${order.number}`).catch(() => null)
      if (next && next.status !== 'pending_payment') {
        setOrder(next)
        clearInterval(timer)
        router.refresh()
      } else if (Date.now() - started > giveUpAfterMs) {
        setWaitedTooLong(true)
        clearInterval(timer)
      }
    }, pollEveryMs)
    return () => clearInterval(timer)
  }, [order.status, order.number, router])

  if (order.status === 'paid') {
    return (
      <section className="result-card success" data-testid="order-paid">
        <div className="result-icon" aria-hidden="true">✓</div>
        <h1>Thank you, your order is confirmed</h1>
        <p>Order <strong>{order.number}</strong> · {formatPrice(order.total)}</p>
        <p className="muted">We sent a confirmation email. Each seller ships their package separately.</p>
        <div className="result-actions">
          <Link href={`/account/orders/${order.number}`} className="btn btn-primary">View order</Link>
          <Link href="/" className="btn btn-ghost">Continue shopping</Link>
        </div>
      </section>
    )
  }

  if (order.status === 'pending_payment') {
    return (
      <section className="result-card pending" data-testid="order-pending">
        <div className="spinner" aria-hidden="true" />
        <h1>{waitedTooLong ? 'We are still waiting for your bank' : 'Confirming your payment…'}</h1>
        <p>Order <strong>{order.number}</strong> · {formatPrice(order.total)}</p>
        {waitedTooLong && order.payment_url && <a href={order.payment_url} className="btn btn-primary">Continue to payment</a>}
      </section>
    )
  }

  return (
    <section className="result-card failed" data-testid="order-failed">
      <div className="result-icon" aria-hidden="true">!</div>
      <h1>Your payment did not go through</h1>
      <p>{failureLabel[order.close_reason ?? ''] ?? 'The payment could not be completed.'}</p>
      <p className="muted">Order {order.number} was not charged. The items are still in your cart.</p>
      <div className="result-actions">
        <Link href="/cart" className="btn btn-primary">Back to cart</Link>
      </div>
    </section>
  )
}
