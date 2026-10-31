import { useState, type FormEvent } from 'react'
import { api, type OrderDetail } from '../api'
import { Badge, Card, FormError, Loading, Pagination } from '../components/ui'
import { formatDate, formatMoney, navigate, useAction, useLoad } from '../lib'

const statuses = ['pending_payment', 'paid', 'payment_failed', 'cancelled']
const labels: Record<string, string> = {
  pending_payment: 'Awaiting payment', paid: 'Paid', payment_failed: 'Payment failed', cancelled: 'Cancelled',
  awaiting_payment: 'Awaiting payment', preparing: 'Preparing', shipped: 'Shipped', delivered: 'Delivered',
  succeeded: 'Succeeded', failed: 'Failed', pending: 'Pending',
}

export function OrderStatusBadge({ status }: { status: string }) {
  const tone = ['paid', 'delivered', 'succeeded'].includes(status) ? 'good'
    : ['pending_payment', 'awaiting_payment', 'pending'].includes(status) ? 'warn'
    : ['payment_failed', 'cancelled', 'failed'].includes(status) ? 'bad' : 'brand'
  return <Badge tone={tone}>{labels[status] ?? status}</Badge>
}

export function Orders({ query }: { query: URLSearchParams }) {
  const status = query.get('status') ?? ''
  const q = query.get('q') ?? ''
  const page = Number(query.get('page') ?? 1) || 1
  const [search, setSearch] = useState(q)
  const orders = useLoad(() => api.orders({ status, q, page }), [status, q, page])
  const go = (changes: Record<string, string | number | undefined>) => navigate('orders', { status, q, page: undefined, ...changes })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    go({ q: search.trim() })
  }

  return (
    <Card title={orders.data ? `${orders.data.total} orders` : 'Orders'}>
      <form className="filters" onSubmit={submit}>
        <input type="search" placeholder="Search by order number or customer email" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search orders" />
        <select value={status} onChange={(e) => go({ status: e.target.value })} aria-label="Status">
          <option value="">All statuses</option>
          {statuses.map((s) => <option key={s} value={s}>{labels[s]}</option>)}
        </select>
        <button type="submit" className="btn btn-ghost">Search</button>
      </form>
      <Loading state={orders}>
        {(data) => (
          <>
            <div className="table-wrap">
              <table className="table" data-testid="orders-table">
                <thead><tr><th>Order</th><th>Customer</th><th>Placed</th><th>Status</th><th className="num">Items</th><th className="num">Sellers</th><th className="num">Total</th></tr></thead>
                <tbody>
                  {data.orders.map((o) => (
                    <tr key={o.number} className="clickable" onClick={() => navigate(`orders/${o.number}`)}>
                      <td><a href={`#/orders/${o.number}`} className="strong">{o.number}</a></td>
                      <td className="muted">{o.customer_email}</td>
                      <td className="muted nowrap">{formatDate(o.placed_at)}</td>
                      <td><OrderStatusBadge status={o.status} /></td>
                      <td className="num">{o.item_count}</td>
                      <td className="num">{o.package_count}</td>
                      <td className="num strong">{formatMoney(o.total)}</td>
                    </tr>
                  ))}
                  {data.orders.length === 0 && <tr><td colSpan={7} className="empty">No orders match these filters.</td></tr>}
                </tbody>
              </table>
            </div>
            <Pagination page={data.page} pageSize={data.page_size} total={data.total} onPage={(p) => go({ page: p })} />
          </>
        )}
      </Loading>
    </Card>
  )
}

function DeliverButton({ id, onDone }: { id: string; onDone: () => void }) {
  const action = useAction()
  const deliver = async () => {
    if (await action.run(() => api.deliverPackage(id))) onDone()
  }
  return (
    <div className="row" data-testid="deliver-package">
      <button className="btn btn-ghost btn-sm" disabled={action.busy} onClick={deliver}>Mark as delivered</button>
      <span className="muted small">Until carrier tracking arrives, delivery is confirmed here. The seller is paid 7 days later.</span>
      <FormError error={action.error} />
    </div>
  )
}

export function OrderDetailPage({ number }: { number: string }) {
  const order = useLoad(() => api.order(number), [number])
  return (
    <Loading state={order}>
      {(o: OrderDetail) => (
        <div className="stack" data-testid="order-detail">
          <Card title={<>Order {o.number} <OrderStatusBadge status={o.status} /></>}>
            <div className="facts">
              <div><span>Customer</span><strong>{o.customer_email}</strong></div>
              <div><span>Placed</span><strong>{formatDate(o.placed_at)}</strong></div>
              <div><span>Paid</span><strong>{o.paid_at ? formatDate(o.paid_at) : '-'}</strong></div>
              <div><span>Total</span><strong>{formatMoney(o.total)}</strong></div>
              {o.close_reason && <div><span>Closed because</span><strong>{o.close_reason}</strong></div>}
              <div><span>Ship to</span><strong>{o.address.full_name}, {o.address.district}/{o.address.city}</strong></div>
            </div>
          </Card>
          {o.packages.map((p, i) => (
            <Card key={p.id} title={<>Package {i + 1} · {p.seller_name} <OrderStatusBadge status={p.status} /></>}>
              <table className="table compact">
                <thead><tr><th>Product</th><th>SKU</th><th className="num">Unit price</th><th className="num">Qty</th><th className="num">Total</th></tr></thead>
                <tbody>
                  {p.lines.map((l) => (
                    <tr key={l.sku}>
                      <td><div className="product-cell">{l.image_url && <img src={l.image_url} alt="" width={40} height={50} />}<div><div className="strong">{l.product_title}</div><div className="muted small">{l.variant_label}</div></div></div></td>
                      <td className="muted">{l.sku}</td>
                      <td className="num">{formatMoney(l.unit_price)}</td>
                      <td className="num">{l.quantity}</td>
                      <td className="num strong">{formatMoney(l.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="muted small">Subtotal {formatMoney(p.subtotal)} · Shipping {p.shipping_fee === 0 ? 'free' : formatMoney(p.shipping_fee)}
                {p.tracking_number && <> · Tracking <strong>{p.tracking_number}</strong></>}</p>
              {p.status === 'shipped' && <DeliverButton id={p.id} onDone={order.reload} />}
            </Card>
          ))}
          <Card title="Payments">
            <table className="table compact" data-testid="payments-table">
              <thead><tr><th>Provider</th><th>Status</th><th>Reason</th><th>Created</th></tr></thead>
              <tbody>
                {o.payments.map((p) => (
                  <tr key={p.created_at}><td>{p.provider}</td><td><OrderStatusBadge status={p.status} /></td><td className="muted">{p.failure_reason ?? '-'}</td><td className="muted">{formatDate(p.created_at)}</td></tr>
                ))}
                {o.payments.length === 0 && <tr><td colSpan={4} className="empty">No payment was started.</td></tr>}
              </tbody>
            </table>
          </Card>
        </div>
      )}
    </Loading>
  )
}
