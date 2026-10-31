import { useState } from 'react'
import { api, type OutboxMessage } from '../api'
import { Badge, Card, FormError, Loading } from '../components/ui'
import { formatCount, formatDate, navigate, plural, useAction, useLoad } from '../lib'

const states = [
  { key: 'failed', label: 'Failed' },
  { key: 'retrying', label: 'Retrying' },
  { key: 'pending', label: 'Pending' },
  { key: 'discarded', label: 'Discarded' },
]

export function Jobs({ query }: { query: URLSearchParams }) {
  const state = query.get('state') ?? 'failed'
  const type = query.get('type') ?? ''
  const data = useLoad(() => api.outbox(state, type || undefined), [state, type])
  const action = useAction()
  const [open, setOpen] = useState<number>()
  const run = async (work: () => Promise<unknown>) => {
    await action.run(work)
    data.reload()
  }
  const filters = (
    <div className="row">
      <select value={state} onChange={(e) => navigate('jobs', { state: e.target.value, type: type || undefined })} aria-label="State">
        {states.map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
      </select>
      <button className="btn btn-ghost" onClick={data.reload}>Refresh</button>
    </div>
  )
  return (
    <Loading state={data}>
      {(d) => {
        const failed = d.types.reduce((sum, t) => sum + t.failed, 0)
        return (
          <div className="stack">
            <FormError error={action.error} />
            <Card title="Background jobs" testId="jobs-summary"
              actions={failed > 0 && (
                <button className="btn btn-primary" disabled={action.busy} data-testid="retry-all"
                  onClick={() => run(() => api.retryOutbox(type || undefined))}>
                  Retry {type ? `failed ${type}` : `all ${plural(failed, 'failed job')}`}
                </button>
              )}>
              <p className="muted small">
                Every side effect (emails, search indexing, ReapDeep sync and events, refunds, payouts) runs from the transactional outbox.
                A job is retried with growing delays and gives up after 10 attempts; admins get an email when that happens.
              </p>
              <table className="table">
                <thead><tr><th>Type</th><th className="num">Pending</th><th className="num">Retrying</th><th className="num">Failed</th><th className="num">Discarded</th><th className="num">Done in 24 h</th></tr></thead>
                <tbody>
                  {d.types.map((t) => (
                    <tr key={t.type} className={t.type === type ? 'selected' : undefined}>
                      <td><a href={`#/jobs?state=${state}&type=${encodeURIComponent(t.type)}`} className="strong">{t.type}</a></td>
                      <td className="num">{formatCount(t.pending)}</td>
                      <td className="num">{t.retrying > 0 ? <Badge tone="warn">{formatCount(t.retrying)}</Badge> : 0}</td>
                      <td className="num">{t.failed > 0 ? <Badge tone="bad">{formatCount(t.failed)}</Badge> : 0}</td>
                      <td className="num muted">{formatCount(t.discarded)}</td>
                      <td className="num muted">{formatCount(t.processed_last_day)}</td>
                    </tr>
                  ))}
                  {d.types.length === 0 && <tr><td colSpan={6} className="empty">No jobs in the last 24 hours.</td></tr>}
                </tbody>
              </table>
            </Card>
            <Card title={`${states.find((s) => s.key === state)?.label ?? state} jobs${type ? ` of type ${type}` : ''}`} actions={filters} testId="jobs-list">
              <table className="table">
                <thead><tr><th>Job</th><th>Attempts</th><th>Last error</th><th>Created</th><th /></tr></thead>
                <tbody>
                  {d.messages.map((m) => (
                    <JobRow key={m.id} message={m} open={open === m.id} onToggle={() => setOpen(open === m.id ? undefined : m.id)}
                      busy={action.busy} onRetry={() => run(() => api.retryOutboxMessage(m.id))} onDiscard={() => run(() => api.discardOutboxMessage(m.id))} />
                  ))}
                  {d.messages.length === 0 && <tr><td colSpan={5} className="empty">Nothing here.</td></tr>}
                </tbody>
              </table>
              {type && <p><a href={`#/jobs?state=${state}`}>Show all types</a></p>}
            </Card>
          </div>
        )
      }}
    </Loading>
  )
}

function JobRow({ message: m, open, onToggle, busy, onRetry, onDiscard }: {
  message: OutboxMessage; open: boolean; onToggle: () => void; busy: boolean; onRetry: () => void; onDiscard: () => void
}) {
  const firstLine = (m.last_error ?? '').split('\n')[0]
  return (
    <>
      <tr data-job={m.id}>
        <td><div className="strong">#{m.id} {m.type}</div><button className="link small" onClick={onToggle}>{open ? 'Hide details' : 'Details'}</button></td>
        <td>{m.attempts}</td>
        <td className="small error-text">{firstLine || <span className="muted">-</span>}</td>
        <td className="muted">{formatDate(m.created_at)}</td>
        <td className="actions">
          {m.failed_at && <button className="btn btn-ghost btn-sm" disabled={busy} onClick={onRetry}>Retry</button>}
          {m.failed_at && !m.discarded_at && <button className="btn btn-ghost btn-sm" disabled={busy} onClick={onDiscard}>Discard</button>}
        </td>
      </tr>
      {open && (
        <tr>
          <td colSpan={5}>
            <div className="small muted">Payload</div>
            <pre className="code">{JSON.stringify(JSON.parse(m.payload), null, 2)}</pre>
            {m.last_error && <><div className="small muted">Last error</div><pre className="code">{m.last_error}</pre></>}
          </td>
        </tr>
      )}
    </>
  )
}
