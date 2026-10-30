import type { Metadata } from 'next'
import { redirect } from 'next/navigation'
import { ChangePasswordForm, SignOutButton } from '@/components/AccountActions'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = { title: 'My account', robots: { index: false } }

export default async function AccountPage() {
  const user = await getCurrentUser()
  if (!user) redirect('/login?next=/account')
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
          <div className="empty-state compact">
            <strong>No orders yet</strong>
            <span>When you place an order, you can track it here.</span>
          </div>
        </section>
        <section className="panel">
          <h2>Change password</h2>
          <ChangePasswordForm />
        </section>
      </div>
    </div>
  )
}
