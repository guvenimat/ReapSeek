import { useState } from 'react'
import { api } from '../api'
import { Card, FormError, Loading } from '../components/ui'
import { formatDate, formatMoney, useAction, useLoad } from '../lib'

export function Payouts() {
  const payouts = useLoad(() => api.payouts(), [])
  const action = useAction()
  const [result, setResult] = useState<string>()

  const run = async () => {
    const r = await action.run(() => api.runPayouts())
    if (r) {
      setResult(`${r.created} payout${r.created === 1 ? '' : 's'} created, ${r.transferred} transferred for the period ending ${formatDate(r.period_end)}.`)
      payouts.reload()
    }
  }

  return (
    <Card
      title="Seller payouts"
      actions={<button className="btn btn-ghost" disabled={action.busy} onClick={run}>Run payouts now</button>}
    >
      <p className="muted small">Every Monday at 00:00 (Turkey time) each seller is paid the ledger entries that became available during the previous weeks,
        that is, 7 days after delivery. Running it again in the same week only retries failed transfers.</p>
      {result && <p className="small" role="status">{result}</p>}
      <FormError error={action.error} />
      <Loading state={payouts}>
        {(list) => (
          <table className="table" data-testid="payouts-table">
            <thead><tr><th>Seller</th><th>Period ends</th><th className="num">Entries</th><th className="num">Amount</th><th>Transfer</th><th>Created</th></tr></thead>
            <tbody>
              {list.map((p) => (
                <tr key={p.id}>
                  <td className="strong">{p.seller_name}</td><td>{formatDate(p.period_end)}</td><td className="num">{p.entry_count}</td>
                  <td className="num strong">{formatMoney(p.amount)}</td><td className="muted">{p.transfer_reference ?? 'pending'}</td><td className="muted">{formatDate(p.created_at)}</td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={6} className="empty">No payouts yet.</td></tr>}
            </tbody>
          </table>
        )}
      </Loading>
    </Card>
  )
}
