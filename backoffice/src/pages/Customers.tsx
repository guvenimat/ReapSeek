import { useState, type FormEvent } from 'react'
import { api, type CustomerAnalytics, type CustomerDetail } from '../api'
import { Badge, Card, FormError, Loading, Pagination, Stat } from '../components/ui'
import { formatCount, formatDate, formatMoney, navigate, useAction, useLoad } from '../lib'
import { OrderStatusBadge } from './Orders'

const sorts = [
  { key: 'recent', label: 'Newest' },
  { key: 'spent', label: 'Top spenders' },
  { key: 'orders', label: 'Most orders' },
  { key: 'last_order', label: 'Last ordered' },
]

const eventLabels: Record<string, string> = {
  product_viewed: 'Viewed', add_to_cart: 'Added to cart', checkout_started: 'Started checkout', purchase: 'Bought', refund: 'Refunded',
  search_performed: 'Searched', search_result_clicked: 'Clicked a result', recommendation_clicked: 'Clicked a recommendation',
  recommendations_shown: 'Saw recommendations', review_submitted: 'Reviewed', question_asked: 'Asked', coupon_applied: 'Applied a coupon',
  cart_reminder_sent: 'Got a cart reminder', $pageview: 'Opened a page',
}

const segmentTone: Record<string, 'good' | 'warn' | 'bad' | 'brand' | 'neutral'> = {
  champions: 'good', loyal: 'good', potential_loyalist: 'brand', new_customers: 'brand', promising: 'brand',
  need_attention: 'warn', about_to_sleep: 'warn', at_risk: 'bad', cant_lose: 'bad', hibernating: 'neutral', lost: 'neutral',
}

export function Customers({ query }: { query: URLSearchParams }) {
  const q = query.get('q') ?? ''
  const sort = query.get('sort') ?? 'recent'
  const status = query.get('status') ?? ''
  const page = Number(query.get('page') ?? 1) || 1
  const [search, setSearch] = useState(q)
  const customers = useLoad(() => api.customers({ q, sort, status, page }), [q, sort, status, page])
  const go = (changes: Record<string, string | number | undefined>) => navigate('customers', { q, sort, status, page: undefined, ...changes })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    go({ q: search.trim() })
  }

  return (
    <Card title={customers.data ? `${formatCount(customers.data.total)} customers` : 'Customers'}>
      <form className="filters" onSubmit={submit}>
        <input type="search" placeholder="Search by name or email" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search customers" />
        <select value={sort} onChange={(e) => go({ sort: e.target.value })} aria-label="Sort">
          {sorts.map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
        </select>
        <select value={status} onChange={(e) => go({ status: e.target.value })} aria-label="Status">
          <option value="">All accounts</option>
          <option value="active">Active</option>
          <option value="suspended">Suspended</option>
        </select>
        <button type="submit" className="btn btn-ghost">Search</button>
      </form>
      <Loading state={customers}>
        {(data) => (
          <>
            <div className="table-wrap">
              <table className="table" data-testid="customers-table">
                <thead><tr><th>Customer</th><th>Joined</th><th className="num">Orders</th><th className="num">Spent</th><th>Last order</th><th /></tr></thead>
                <tbody>
                  {data.customers.map((c) => (
                    <tr key={c.id} className="clickable" onClick={() => navigate(`customers/${c.id}`)} data-customer={c.id}>
                      <td><a href={`#/customers/${c.id}`} className="strong">{c.name}</a><div className="muted small">{c.email}</div></td>
                      <td className="muted nowrap">{formatDate(c.created_at)}</td>
                      <td className="num">{formatCount(c.orders)}</td>
                      <td className="num strong">{formatMoney(c.spent)}</td>
                      <td className="muted nowrap">{c.last_order_at ? formatDate(c.last_order_at) : '-'}</td>
                      <td>{c.suspended_at && <Badge tone="bad">Suspended</Badge>}</td>
                    </tr>
                  ))}
                  {data.customers.length === 0 && <tr><td colSpan={6} className="empty">No customers match these filters.</td></tr>}
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

export function CustomerDetailPage({ id }: { id: number }) {
  const customer = useLoad(() => api.customer(id), [id])
  const analytics = useLoad(() => api.customerAnalytics(id), [id])
  return (
    <Loading state={customer}>
      {(c) => (
        <div className="stack" data-testid="customer-detail">
          <Profile customer={c} onChanged={customer.reload} />
          <div className="stats">
            <Stat label="Orders" value={formatCount(c.stats.orders)} />
            <Stat label="Spent" value={formatMoney(c.stats.spent)} />
            <Stat label="Average order" value={formatMoney(c.stats.average_order)} />
            <Stat label="Refunded" value={formatMoney(c.stats.refunded)} tone={c.stats.refunded > 0 ? 'warn' : undefined} />
          </div>
          <Loading state={analytics}>{(a) => <Behaviour analytics={a} distinctId={c.distinct_id} />}</Loading>
          <Card title="Orders">
            <table className="table">
              <thead><tr><th>Order</th><th>Placed</th><th>Status</th><th className="num">Sellers</th><th className="num">Total</th></tr></thead>
              <tbody>
                {c.orders.map((o) => (
                  <tr key={o.number} className="clickable" onClick={() => navigate(`orders/${o.number}`)}>
                    <td><a href={`#/orders/${o.number}`} className="strong">{o.number}</a></td>
                    <td className="muted nowrap">{formatDate(o.placed_at)}</td>
                    <td><OrderStatusBadge status={o.status} /></td>
                    <td className="num">{o.packages}</td>
                    <td className="num strong">{formatMoney(o.total)}</td>
                  </tr>
                ))}
                {c.orders.length === 0 && <tr><td colSpan={5} className="empty">No orders yet.</td></tr>}
              </tbody>
            </table>
          </Card>
          <div className="grid-2">
            <Card title="Returns">
              <table className="table">
                <thead><tr><th>Return</th><th>Status</th><th className="num">Refund</th></tr></thead>
                <tbody>
                  {c.returns.map((r) => (
                    <tr key={r.number}><td><div className="strong">{r.number}</div><div className="muted small">{r.order_number} · {formatDate(r.requested_at)}</div></td>
                      <td><OrderStatusBadge status={r.status} /></td><td className="num">{formatMoney(r.refund_amount)}</td></tr>
                  ))}
                  {c.returns.length === 0 && <tr><td colSpan={3} className="empty">No returns.</td></tr>}
                </tbody>
              </table>
            </Card>
            <Card title="Coupons">
              <table className="table">
                <thead><tr><th>Code</th><th>Offer</th><th>Status</th></tr></thead>
                <tbody>
                  {c.coupons.map((k) => (
                    <tr key={k.code}><td><div className="strong mono">{k.code}</div><div className="muted small">{k.campaign}</div></td>
                      <td>{k.kind === 'percent' ? `${k.value}%` : formatMoney(k.value)}</td>
                      <td>{k.used_order_number ? <Badge tone="good">Used on {k.used_order_number}</Badge>
                        : new Date(k.expires_at) < new Date() ? <Badge>Expired</Badge> : <Badge tone="brand">Until {formatDate(k.expires_at)}</Badge>}</td></tr>
                  ))}
                  {c.coupons.length === 0 && <tr><td colSpan={3} className="empty">No coupons.</td></tr>}
                </tbody>
              </table>
            </Card>
          </div>
          <Card title="Reviews">
            <table className="table">
              <thead><tr><th>Product</th><th>Review</th><th>Status</th><th>Written</th></tr></thead>
              <tbody>
                {c.reviews.map((r) => (
                  <tr key={`${r.product_id}`}><td className="strong">{r.product_title}</td>
                    <td><span className="stars-text">{'★'.repeat(r.rating)}{'☆'.repeat(5 - r.rating)}</span> {r.title}</td>
                    <td><Badge tone={r.status === 'visible' ? 'good' : 'bad'}>{r.status}</Badge></td><td className="muted nowrap">{formatDate(r.created_at)}</td></tr>
                ))}
                {c.reviews.length === 0 && <tr><td colSpan={4} className="empty">No reviews.</td></tr>}
              </tbody>
            </table>
          </Card>
        </div>
      )}
    </Loading>
  )
}

function Profile({ customer: c, onChanged }: { customer: CustomerDetail; onChanged: () => void }) {
  const action = useAction()
  const [reason, setReason] = useState('')
  const [resetSent, setResetSent] = useState(false)
  const sendReset = async () => {
    if ((await action.run(() => api.sendCustomerReset(c.id))) !== undefined) setResetSent(true)
  }
  const suspend = async () => {
    await action.run(() => api.suspendCustomer(c.id, reason.trim()))
    setReason('')
    onChanged()
  }
  const reinstate = async () => {
    await action.run(() => api.reinstateCustomer(c.id))
    onChanged()
  }
  return (
    <Card title={<>{c.name} {c.suspended_at && <Badge tone="bad">Suspended</Badge>}</>} testId="customer-profile">
      <div className="facts">
        <div><span className="muted small">Email</span><div>{c.email}</div>
          <span>{c.email_verified_at ? <Badge tone="good">Verified</Badge> : <Badge tone="warn">Not verified</Badge>}</span></div>
        <div><span className="muted small">Joined</span><div>{formatDate(c.created_at)}</div></div>
        <div><span className="muted small">First order</span><div>{c.stats.first_order_at ? formatDate(c.stats.first_order_at) : '-'}</div></div>
        <div><span className="muted small">ReapDeep id</span><div className="mono">{c.distinct_id}</div></div>
      </div>
      <FormError error={action.error} />
      {c.suspended_at ? (
        <div className="review-box">
          <div className="small"><strong>Suspended {formatDate(c.suspended_at)}:</strong> {c.suspended_reason}</div>
          <div className="row"><button className="btn btn-primary btn-sm" disabled={action.busy} onClick={reinstate}>Reinstate account</button></div>
        </div>
      ) : (
        <form className="filters" onSubmit={(e) => { e.preventDefault(); if (reason.trim()) suspend() }}>
          <input placeholder="Reason (required to suspend)" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} aria-label="Suspension reason" />
          <button type="submit" className="btn btn-ghost danger" disabled={action.busy || !reason.trim()}>Suspend account</button>
        </form>
      )}
      {!c.suspended_at && (
        <p className="small">
          {resetSent ? <span data-testid="reset-sent">A password reset link was emailed to the customer.</span>
            : <button className="link" disabled={action.busy} onClick={sendReset} data-testid="send-reset">Email a password reset link</button>}
          <span className="muted"> · Staff never see or set customer passwords.</span>
        </p>
      )}
      <p className="muted small">Suspending signs the customer out everywhere and blocks storefront sign-in. Orders already placed are not affected.</p>
    </Card>
  )
}

function Behaviour({ analytics: a, distinctId }: { analytics: CustomerAnalytics; distinctId: string }) {
  if (a.status === 'not_connected') return <Card title="Behaviour in ReapDeep"><p className="muted">ReapDeep is not connected.</p></Card>
  if (a.status === 'unavailable') return <Card title="Behaviour in ReapDeep"><p className="muted">ReapDeep is not reachable right now.</p></Card>
  return (
    <Card title="Behaviour in ReapDeep" testId="customer-reapdeep">
      {a.rfm ? (
        <div className="facts">
          <div><span className="muted small">RFM segment</span><span><Badge tone={segmentTone[a.rfm.segment] ?? 'neutral'}>{a.rfm.segment_name}</Badge></span></div>
          <div><span className="muted small">Recency / frequency</span><div>R{a.rfm.r} · F{a.rfm.f}</div></div>
          <div><span className="muted small">Last purchase</span><div>{a.rfm.recency_days === 0 ? 'today' : `${a.rfm.recency_days} days ago`}</div></div>
          <div><span className="muted small">Orders, net revenue ({a.rfm.window_days} days)</span><div>{a.rfm.orders} · {formatMoney(a.rfm.monetary)}</div></div>
        </div>
      ) : <p className="muted">No purchases in ReapDeep yet, so there is no RFM segment for {distinctId}.</p>}
      <table className="table">
        <thead><tr><th>When</th><th>What</th><th>Product</th><th>Details</th></tr></thead>
        <tbody>
          {a.activity.map((e, i) => (
            <tr key={i}><td className="muted nowrap">{formatDate(e.timestamp)}</td><td>{eventLabels[e.event] ?? e.event}</td>
              <td>{e.product_title ?? (e.item_id ? `#${e.item_id}` : '')}</td><td className="muted small">{e.detail ?? ''}</td></tr>
          ))}
          {a.activity.length === 0 && <tr><td colSpan={4} className="empty">No events yet.</td></tr>}
        </tbody>
      </table>
    </Card>
  )
}
