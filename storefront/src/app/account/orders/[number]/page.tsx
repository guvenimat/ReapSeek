import type { Metadata } from 'next'
import Link from 'next/link'
import { notFound, redirect } from 'next/navigation'
import { formatPrice } from '@/lib/catalog'
import { ReturnForm } from '@/components/orders/ReturnForm'
import { Tracking } from '@/components/orders/Tracking'
import { returnReasons, returnStatusLabel } from '@/lib/order-types'
import { failureLabel, getOrder, orderStatusLabel } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

type Props = { params: Promise<{ number: string }> }

export const metadata: Metadata = { title: 'Order', robots: { index: false } }

const day = (value: string) => new Date(value).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', timeZone: 'Europe/Istanbul' })
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
            <section key={p.id} className="panel" data-package={p.id}>
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
              <p className="muted small">Shipping {p.shipping_fee === 0 ? 'free' : formatPrice(p.shipping_fee)}</p>
              {p.tracking_number && <Tracking number={p.tracking_number} current={p.shipment_status} events={p.tracking} startedAt={p.shipped_at} />}
              {p.returnable && p.return_deadline && <ReturnForm orderNumber={order.number} pkg={p} deadline={day(p.return_deadline)} />}
              {!p.returnable && p.return_deadline && <p className="muted small">The return window closed on {day(p.return_deadline)}.</p>}
            </section>
          ))}
          {order.returns.length > 0 && (
            <section className="panel" data-testid="returns">
              <header className="package-head"><strong>Returns</strong></header>
              {order.returns.map((r) => (
                <div key={r.id} className="return-item" data-return={r.number}>
                  <div className="return-item-head">
                    <span><strong>{r.number}</strong> · {r.seller_name} · {returnReasons[r.reason] ?? r.reason}</span>
                    <span className={`status-pill return-${r.status}`} data-testid="return-status">{returnStatusLabel[r.status] ?? r.status}</span>
                  </div>
                  <ul className="return-lines">
                    {r.lines.map((l) => <li key={l.order_line_id}>{l.quantity} × {l.product_title} <span className="muted small">{l.variant_label}</span></li>)}
                  </ul>
                  <p className="small">Refund {formatPrice(r.total)}{r.refunded_at ? ` · refunded ${date(r.refunded_at)}` : ''}</p>
                  {r.status === 'approved' && <p className="notice">Drop the parcel at any Sandbox Cargo point with the return code <strong>{r.return_tracking}</strong>.</p>}
                  {r.status === 'rejected' && <p className="notice warn">The seller did not accept this return: {r.rejection_note}</p>}
                  {r.return_tracking && r.status !== 'approved' && <Tracking number={r.return_tracking} current={r.tracking.at(-1)?.status ?? null} events={r.tracking} startedAt={null} />}
                </div>
              ))}
            </section>
          )}
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
