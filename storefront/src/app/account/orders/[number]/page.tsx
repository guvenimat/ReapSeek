import type { Metadata } from 'next'
import Link from 'next/link'
import { notFound, redirect } from 'next/navigation'
import { formatPrice } from '@/lib/catalog'
import { failureLabel, getOrder, orderStatusLabel } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

type Props = { params: Promise<{ number: string }> }

export const metadata: Metadata = { title: 'Order', robots: { index: false } }

const date = (value: string) => new Date(value).toLocaleString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Istanbul' })

export default async function OrderPage({ params }: Props) {
  const { number } = await params
  if (!(await getCurrentUser())) redirect(`/login?next=/account/orders/${encodeURIComponent(number)}`)
  const order = await getOrder(number)
  if (!order) notFound()
  return (
    <div className="container order-page" data-testid="order-detail">
      <nav className="breadcrumb" aria-label="Breadcrumb">
        <Link href="/account">My account</Link>
        <span className="crumb"><span>/</span><span>Order {order.number}</span></span>
      </nav>
      <div className="order-head panel">
        <div>
          <h1>Order {order.number}</h1>
          <p className="muted">Placed {date(order.placed_at)}{order.paid_at ? ` · Paid ${date(order.paid_at)}` : ''}</p>
        </div>
        <span className={`status-pill ${order.status}`} data-testid="order-status">{orderStatusLabel[order.status] ?? order.status}</span>
      </div>
      {order.status === 'payment_failed' || order.status === 'cancelled' ? (
        <p className="notice warn">{failureLabel[order.close_reason ?? ''] ?? 'This order was closed.'} You were not charged.</p>
      ) : null}
      {order.status === 'pending_payment' && order.payment_url && (
        <p className="notice">Waiting for payment. <a href={order.payment_url} className="inline-link">Continue to payment</a></p>
      )}
      <div className="order-grid">
        <div className="order-packages">
          {order.packages.map((p, i) => (
            <section key={p.seller_id} className="panel">
              <header className="package-head">
                <strong>Package {i + 1} · {p.seller_name}</strong>
                <span className={`status-pill ${p.status}`}>{orderStatusLabel[p.status] ?? p.status}</span>
              </header>
              {p.lines.map((l) => (
                <div key={l.sku} className="checkout-line">
                  {l.image_url && <img src={l.image_url} alt="" width={48} height={60} />}
                  <span>{l.quantity} × {l.product_title}<br /><span className="muted small">{l.variant_label} · {l.sku}</span></span>
                  <strong>{formatPrice(l.total)}</strong>
                </div>
              ))}
              <p className="muted small">Shipping {p.shipping_fee === 0 ? 'free' : formatPrice(p.shipping_fee)}{p.tracking_number ? ` · Tracking ${p.tracking_number}` : ''}</p>
            </section>
          ))}
        </div>
        <aside className="order-summary">
          <h2>Summary</h2>
          <dl>
            <div><dt>Items</dt><dd>{formatPrice(order.subtotal)}</dd></div>
            <div><dt>Shipping</dt><dd>{order.shipping_total === 0 ? 'Free' : formatPrice(order.shipping_total)}</dd></div>
            <div className="total"><dt>Total</dt><dd>{formatPrice(order.total)}</dd></div>
          </dl>
          <h3>Delivery address</h3>
          <p className="small">{order.address.full_name}<br />{order.address.line}<br />{order.address.district}/{order.address.city} {order.address.postal_code}<br />{order.address.phone}</p>
        </aside>
      </div>
    </div>
  )
}
