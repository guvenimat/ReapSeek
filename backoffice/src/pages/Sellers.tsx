import { useState, type FormEvent } from 'react'
import { api, type Seller } from '../api'
import { Card, FormError, Loading, StatusBadge } from '../components/ui'
import { formatCount, useAction, useLoad } from '../lib'

export function Sellers() {
  const sellers = useLoad(() => api.sellers(), [])
  const action = useAction()
  const [form, setForm] = useState({ name: '', city: '', commission: '15' })

  const create = async (e: FormEvent) => {
    e.preventDefault()
    if (await action.run(() => api.createSeller({ name: form.name, city: form.city, commission_rate: Number(form.commission) / 100 }))) {
      setForm({ name: '', city: '', commission: '15' })
      sellers.reload()
    }
  }

  return (
    <Card title={sellers.data ? `${sellers.data.length} sellers` : 'Sellers'}>
      <Loading state={sellers}>
        {(list) => (
          <div className="table-wrap">
            <table className="table" data-testid="sellers-table">
              <thead><tr><th>Seller</th><th>City</th><th className="num">Rating</th><th className="num">Commission</th><th className="num">Products</th><th className="num">Offers</th><th>Status</th><th /></tr></thead>
              <tbody>{list.map((s) => <SellerRow key={s.id} seller={s} onChanged={sellers.reload} />)}</tbody>
            </table>
          </div>
        )}
      </Loading>
      <form className="inline-form" onSubmit={create} data-testid="add-seller">
        <div className="form-title">Add a seller</div>
        <div className="row wrap">
          <input required maxLength={80} placeholder="Store name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} aria-label="Store name" />
          <input maxLength={60} placeholder="City" value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} aria-label="City" />
          <label className="inline-label">Commission
            <input type="number" min="0" max="50" step="0.5" value={form.commission} onChange={(e) => setForm({ ...form, commission: e.target.value })} aria-label="Commission percent" /> %
          </label>
          <button type="submit" className="btn btn-primary" disabled={action.busy}>Add seller</button>
        </div>
        <FormError error={action.error} />
      </form>
    </Card>
  )
}

function SellerRow({ seller, onChanged }: { seller: Seller; onChanged: () => void }) {
  const action = useAction()
  const toggle = async () => {
    const next = seller.status === 'active' ? 'suspended' : 'active'
    if (next === 'suspended' && !window.confirm(`Suspend ${seller.name}? Their offers stop winning the buy box immediately.`)) return
    if (await action.run(() => api.updateSeller(seller.id, { status: next }))) onChanged()
  }
  return (
    <tr data-seller={seller.id}>
      <td><div className="strong">{seller.name}</div>{action.error && <div className="form-error small">{action.error}</div>}</td>
      <td className="muted">{seller.city || '-'}</td>
      <td className="num">★ {seller.rating.toFixed(1)}</td>
      <td className="num">{(seller.commission_rate * 100).toFixed(1)}%</td>
      <td className="num">{formatCount(seller.product_count)}</td>
      <td className="num">{formatCount(seller.offer_count)}</td>
      <td><StatusBadge status={seller.status} /></td>
      <td className="actions">
        <button className={`btn btn-ghost btn-sm${seller.status === 'active' ? ' danger' : ''}`} disabled={action.busy} onClick={toggle}>
          {seller.status === 'active' ? 'Suspend' : 'Reactivate'}
        </button>
      </td>
    </tr>
  )
}
