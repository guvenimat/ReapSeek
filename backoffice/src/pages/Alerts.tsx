import { api } from '../api'
import { Badge, Card, Loading } from '../components/ui'
import { formatDate, plural, useLoad } from '../lib'

const conditionLabel: Record<string, string> = { above: 'above', below: 'below', increase: 'rises by', decrease: 'drops by' }

export function Alerts() {
  const alerts = useLoad(() => api.alerts(), [])
  return (
    <Card title="ReapDeep alerts" actions={<button className="btn btn-ghost" onClick={alerts.reload}>Refresh</button>}>
      <Loading state={alerts}>
        {(data) => (
          <>
            <p className="muted small">
              Alerts are defined in ReapDeep on saved trends and delivered here as signed webhooks; admins also get an email.
              {' '}{plural(data.subscriptions, 'alert')} {data.subscriptions === 1 ? 'is' : 'are'} connected.
            </p>
            <table className="table" data-testid="alerts-table">
              <thead><tr><th>Alert</th><th>State</th><th>What happened</th><th>Rule</th><th>Checked</th></tr></thead>
              <tbody>
                {data.events.map((e) => (
                  <tr key={e.id} data-alert-event={e.id}>
                    <td><div className="strong">{e.alert_name}</div><div className="muted small">{e.insight_name}</div></td>
                    <td><Badge tone={e.state === 'firing' ? 'bad' : 'good'}>{e.state === 'firing' ? 'firing' : 'resolved'}</Badge></td>
                    <td>{e.message}</td>
                    <td className="muted small">{conditionLabel[e.condition] ?? e.condition} {e.threshold}{e.condition === 'increase' || e.condition === 'decrease' ? '%' : ''} per {e.time_window}</td>
                    <td className="muted">{formatDate(e.checked_at)}</td>
                  </tr>
                ))}
                {data.events.length === 0 && <tr><td colSpan={5} className="empty">No alerts have fired yet.</td></tr>}
              </tbody>
            </table>
          </>
        )}
      </Loading>
    </Card>
  )
}
