import { useEffect, useState } from 'react'
import { api, type User } from '../api'
import { Card, Loading, Stat } from '../components/ui'
import { formatCount, useLoad } from '../lib'

export function Overview({ user }: { user: User }) {
  const [health, setHealth] = useState<string>('checking')
  const stats = useLoad(() => api.stats(), [])

  useEffect(() => {
    api.health().then((h) => setHealth(h.status)).catch(() => setHealth('unreachable'))
  }, [])

  return (
    <div className="stack">
      <Card title={`Welcome, ${user.name}`}>
        <p className="muted">Manage the catalog, sellers and their offers. Orders, customers and marketing arrive in later releases.</p>
      </Card>
      <Loading state={stats}>
        {(s) => (
          <div className="stats" data-testid="catalog-stats">
            <Stat label="Active products" value={formatCount(s.active_products)} testId="stat-active-products" />
            <Stat label="Drafts" value={formatCount(s.draft_products)} />
            <Stat label="Out of stock" value={formatCount(s.out_of_stock_products)} tone={s.out_of_stock_products > 0 ? 'warn' : undefined} />
            <Stat label="Offers" value={formatCount(s.offers)} />
            <Stat label="Active sellers" value={formatCount(s.active_sellers)} />
            <Stat label="Suspended sellers" value={formatCount(s.suspended_sellers)} tone={s.suspended_sellers > 0 ? 'bad' : undefined} />
            <Stat label="Brands" value={formatCount(s.brands)} />
            <Stat label="Categories" value={formatCount(s.categories)} />
          </div>
        )}
      </Loading>
      <Card title="Platform status" testId="platform-status">
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
      </Card>
    </div>
  )
}
