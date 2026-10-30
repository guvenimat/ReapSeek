import type { Metadata } from 'next'
import Link from 'next/link'
import { redirect } from 'next/navigation'
import { ChangePasswordForm, SignOutButton } from '@/components/AccountActions'
import { formatPrice } from '@/lib/catalog'
import { getOrders, orderStatusLabel } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = { title: 'My account', robots: { index: false } }

export default async function AccountPage() {
  const user = await getCurrentUser()
  if (!user) redirect('/login?next=/account')
  const orders = await getOrders()
  return (
    <div className="container account-page">
      <div className="account-head">
        <div className="avatar" aria-hidden="true">{user.name.charAt(0).toUpperCase()}</div>
        <div>
          <h1 data-testid="account-name">{user.name}</h1>
          <p className="muted" data-testid="account-email">{user.email}</p>
        </div>
        <SignOutButton />
      </div>
      <div className="account-grid">
        <section className="panel">
          <h2>Orders</h2>
          {orders.length === 0 ? (
            <div className="empty-state compact">
              <strong>No orders yet</strong>
              <span>When you place an order, you can track it here.</span>
            </div>
          ) : (
            <div className="order-list" data-testid="order-list">
              {orders.map((o) => (
                <Link key={o.number} href={`/account/orders/${o.number}`} className="order-row" data-order={o.number}>
                  {o.image_url && <img src={o.image_url} alt="" width={48} height={60} />}
                  <span>
                    <strong>{o.number}</strong>
                    <span className="muted small">{new Date(o.placed_at).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Europe/Istanbul' })} · {o.item_count} {o.item_count === 1 ? 'item' : 'items'}</span>
                  </span>
                  <span className={`status-pill ${o.status}`}>{orderStatusLabel[o.status] ?? o.status}</span>
                  <strong>{formatPrice(o.total)}</strong>
                </Link>
              ))}
            </div>
          )}
        </section>
        <section className="panel">
          <h2>Change password</h2>
          <ChangePasswordForm />
        </section>
      </div>
    </div>
  )
}
