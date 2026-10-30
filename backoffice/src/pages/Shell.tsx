import { useEffect, useState, type ReactNode } from 'react'
import { api, type User } from '../api'
import { BoxIcon, HomeIcon, LogOutIcon, LogoMark, MegaphoneIcon, ReceiptIcon, StoreIcon, UsersIcon } from '../components/icons'

const sections: { label: string; icon: () => ReactNode; ready: boolean }[] = [
  { label: 'Overview', icon: HomeIcon, ready: true },
  { label: 'Catalog', icon: BoxIcon, ready: false },
  { label: 'Sellers', icon: StoreIcon, ready: false },
  { label: 'Orders', icon: ReceiptIcon, ready: false },
  { label: 'Customers', icon: UsersIcon, ready: false },
  { label: 'Marketing', icon: MegaphoneIcon, ready: false },
]

export function Shell({ user, onSignedOut }: { user: User; onSignedOut: () => void }) {
  async function signOut() {
    await api.logout().catch(() => undefined)
    onSignedOut()
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <LogoMark />
          <div>
            <strong>ReapSeek</strong>
            <span>Back office</span>
          </div>
        </div>
        <nav aria-label="Sections">
          {sections.map(({ label, icon: IconComponent, ready }) => (
            <span key={label} className={`nav-item${label === 'Overview' ? ' active' : ''}${ready ? '' : ' disabled'}`}
              aria-disabled={!ready}>
              <IconComponent />
              {label}
              {!ready && <em>Soon</em>}
            </span>
          ))}
        </nav>
      </aside>
      <div className="content">
        <header className="topbar">
          <h1>Overview</h1>
          <div className="topbar-user">
            <div className="who">
              <strong data-testid="user-name">{user.name}</strong>
              <span>{user.email}</span>
            </div>
            <button type="button" className="btn btn-ghost" onClick={signOut} data-testid="sign-out">
              <LogOutIcon /> Sign out
            </button>
          </div>
        </header>
        <main className="page">
          <Overview user={user} />
        </main>
      </div>
    </div>
  )
}

function Overview({ user }: { user: User }) {
  const [health, setHealth] = useState<string>('checking')

  useEffect(() => {
    api.health().then((h) => setHealth(h.status)).catch(() => setHealth('unreachable'))
  }, [])

  return (
    <div className="cards">
      <section className="card welcome">
        <h2>Welcome, {user.name}</h2>
        <p>Catalog, seller and order management arrive here stage by stage. The storefront and this back office share one API.</p>
      </section>
      <section className="card" data-testid="platform-status">
        <h2>Platform status</h2>
        <dl className="status-list">
          <div>
            <dt>API</dt>
            <dd><span className={`dot ${health === 'ok' ? 'ok' : health === 'checking' ? '' : 'bad'}`} />{health}</dd>
          </div>
          <div>
            <dt>Signed in as</dt>
            <dd>{user.role}</dd>
          </div>
        </dl>
      </section>
    </div>
  )
}
