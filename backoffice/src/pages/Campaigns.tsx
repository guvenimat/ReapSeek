import { useState, type FormEvent } from 'react'
import { api, type Campaign } from '../api'
import { Badge, Card, FormError, Loading } from '../components/ui'
import { formatCount, formatDate, formatMoney, useAction, useLoad } from '../lib'

const empty = { name: '', cohort_id: 0, kind: 'percent', value: '10', min_subtotal: '0', max_discount: '', valid_days: '14', subject: '', message: '' }

export function Campaigns() {
  const campaigns = useLoad(() => api.campaigns(), [])
  const cohorts = useLoad(() => api.audienceCohorts(), [])
  const action = useAction()
  const [form, setForm] = useState(empty)
  const [audience, setAudience] = useState<{ customers: number; unreachable: number }>()

  const pickCohort = async (id: number) => {
    setForm({ ...form, cohort_id: id })
    setAudience(undefined)
    if (id) {
      const preview = await action.run(() => api.audiencePreview(id))
      if (preview) setAudience(preview)
    }
  }

  const create = async (e: FormEvent) => {
    e.preventDefault()
    const created = await action.run(() => api.createCampaign({
      name: form.name, cohort_id: form.cohort_id, kind: form.kind, value: Number(form.value), min_subtotal: Number(form.min_subtotal || 0),
      max_discount: form.max_discount ? Number(form.max_discount) : null, valid_days: Number(form.valid_days), subject: form.subject, message: form.message,
    }))
    if (created) {
      setForm(empty)
      setAudience(undefined)
      campaigns.reload()
    }
  }

  const send = async (c: Campaign) => {
    if (!window.confirm(`Send "${c.name}" to ${c.cohort_name}? Every customer in the cohort gets a personal code by email.`)) return
    if (await action.run(() => api.sendCampaign(c.id))) campaigns.reload()
  }

  return (
    <div className="stack">
      <Card title="Campaigns" actions={<button className="btn btn-ghost" onClick={campaigns.reload}>Refresh</button>}>
        <p className="muted small">Audiences come from ReapDeep cohorts (RFM segments or any rule built in ReapDeep). Each customer gets a personal single-use code; ReapSeek pays the discount, sellers are settled on the full price.</p>
        <FormError error={action.error} />
        <Loading state={campaigns}>
          {(list) => (
            <table className="table" data-testid="campaigns-table">
              <thead><tr><th>Campaign</th><th>Audience</th><th>Offer</th><th className="num">Sent</th><th className="num">Used</th><th className="num">Revenue</th><th>Status</th><th /></tr></thead>
              <tbody>
                {list.map((c) => (
                  <tr key={c.id} data-campaign={c.id}>
                    <td><div className="strong">{c.name}</div><div className="muted small">{c.subject}</div></td>
                    <td>{c.cohort_name}</td>
                    <td>{c.kind === 'percent' ? `${c.value}%` : formatMoney(c.value)}{c.min_subtotal > 0 && <div className="muted small">over {formatMoney(c.min_subtotal)}</div>}</td>
                    <td className="num">{formatCount(c.recipients)}</td>
                    <td className="num">{formatCount(c.redeemed)}</td>
                    <td className="num">{formatMoney(c.revenue)}</td>
                    <td><Badge tone={c.status === 'sent' ? 'good' : c.status === 'failed' ? 'bad' : 'warn'}>{c.status}</Badge>
                      {c.error && <div className="form-error small">{c.error}</div>}{c.sent_at && <div className="muted small">{formatDate(c.sent_at)}</div>}</td>
                    <td className="actions">{(c.status === 'draft' || c.status === 'failed') && <button className="btn btn-primary btn-sm" disabled={action.busy} onClick={() => void send(c)}>Send</button>}</td>
                  </tr>
                ))}
                {list.length === 0 && <tr><td colSpan={8} className="empty">No campaigns yet.</td></tr>}
              </tbody>
            </table>
          )}
        </Loading>
      </Card>
      <Card title="New campaign">
        <Loading state={cohorts}>
          {(list) => (
            <form className="form" onSubmit={create} data-testid="new-campaign">
              <label className="field wide"><span>Name</span><input required maxLength={100} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
              <label className="field"><span>Audience (ReapDeep cohort)</span>
                <select required value={form.cohort_id} onChange={(e) => void pickCohort(Number(e.target.value))}>
                  <option value={0}>Choose a cohort…</option>
                  {list.map((c) => <option key={c.id} value={c.id}>{c.name}{c.member_count != null ? ` (${c.member_count} people)` : ''}</option>)}
                </select>
              </label>
              <div className="field"><span>Reach</span><strong data-testid="audience-size">{audience === undefined ? '-' : `${formatCount(audience.customers)} customers with a verified email`}</strong>{audience && audience.unreachable > 0 && <span className="muted small">{formatCount(audience.unreachable)} without a verified email or suspended are left out</span>}</div>
              <label className="field"><span>Discount</span>
                <div className="row">
                  <input type="number" min="1" step="0.01" required value={form.value} onChange={(e) => setForm({ ...form, value: e.target.value })} aria-label="Discount value" />
                  <select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })} aria-label="Discount kind"><option value="percent">%</option><option value="fixed">TRY</option></select>
                </div>
              </label>
              <label className="field"><span>Minimum basket (TRY)</span><input type="number" min="0" step="0.01" value={form.min_subtotal} onChange={(e) => setForm({ ...form, min_subtotal: e.target.value })} /></label>
              <label className="field"><span>Discount cap (TRY, optional)</span><input type="number" min="1" step="0.01" value={form.max_discount} onChange={(e) => setForm({ ...form, max_discount: e.target.value })} /></label>
              <label className="field"><span>Valid for (days)</span><input type="number" min="1" max="90" required value={form.valid_days} onChange={(e) => setForm({ ...form, valid_days: e.target.value })} /></label>
              <label className="field wide"><span>Email subject</span><input required maxLength={120} value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} /></label>
              <label className="field wide"><span>Email message</span><textarea required rows={3} maxLength={2000} value={form.message} onChange={(e) => setForm({ ...form, message: e.target.value })} /></label>
              <div className="form-actions wide"><button type="submit" className="btn btn-primary" disabled={action.busy || !form.cohort_id}>Save as draft</button></div>
            </form>
          )}
        </Loading>
      </Card>
    </div>
  )
}
