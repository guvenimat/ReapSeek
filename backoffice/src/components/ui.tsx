import type { ReactNode } from 'react'
import type { Loadable } from '../lib'

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'good' | 'warn' | 'bad' | 'brand'; children: ReactNode }) {
  return <span className={`badge ${tone}`}>{children}</span>
}

export function StatusBadge({ status }: { status: string }) {
  const tone = status === 'active' ? 'good' : status === 'draft' || status === 'paused' ? 'warn' : status === 'archived' || status === 'suspended' ? 'bad' : 'neutral'
  return <Badge tone={tone}>{status}</Badge>
}

export function Card({ title, actions, children, testId }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; testId?: string }) {
  return (
    <section className="card" data-testid={testId}>
      {(title || actions) && (
        <header className="card-head">
          {title && <h2>{title}</h2>}
          {actions && <div className="card-actions">{actions}</div>}
        </header>
      )}
      {children}
    </section>
  )
}

export function Loading<T>({ state, children }: { state: Loadable<T>; children: (data: T) => ReactNode }) {
  if (state.error) return <p className="form-error" role="alert">{state.error}</p>
  if (state.data === undefined) return <div className="loading" aria-busy="true">Loading…</div>
  return <div className={state.loading ? 'stale' : undefined}>{children(state.data)}</div>
}

export function FormError({ error }: { error?: string }) {
  return error ? <p className="form-error" role="alert">{error}</p> : null
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  return (
    <div className="pagination">
      <span className="muted">Page {page} of {pages}</span>
      <button type="button" className="btn btn-ghost btn-sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
      <button type="button" className="btn btn-ghost btn-sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</button>
    </div>
  )
}

export function Stat({ label, value, tone, testId }: { label: string; value: ReactNode; tone?: 'warn' | 'bad'; testId?: string }) {
  return (
    <div className={`stat${tone ? ` ${tone}` : ''}`} data-testid={testId}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}
