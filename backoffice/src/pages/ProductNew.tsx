import { useState, type FormEvent } from 'react'
import { api, sellerApi } from '../api'
import { TrashIcon } from '../components/icons'
import { Card, FormError, Loading } from '../components/ui'
import { navigate, useAction, useLoad } from '../lib'
import { CategorySelect } from './ProductDetail'

type Axis = { name: string; values: string }

export function ProductNew({ seller = false }: { seller?: boolean }) {
  const lookups = useLoad(() => seller ? Promise.all([sellerApi.brands(), sellerApi.categories()]) : Promise.all([api.brands(), api.categories()]), [seller])
  const action = useAction()
  const [form, setForm] = useState({ title: '', brand_id: 0, category_id: 0, description: '', brand_name: '' })
  const [axes, setAxes] = useState<Axis[]>([{ name: 'Color', values: '' }, { name: 'Size', values: '' }])

  const parsedAxes = axes
    .map((a) => ({ name: a.name.trim(), values: a.values.split(',').map((v) => v.trim()).filter(Boolean) }))
    .filter((a) => a.name && a.values.length > 0)
  const variantCount = parsedAxes.reduce((n, a) => n * a.values.length, 1)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const body = (brandId: number) => ({
      title: form.title, brand_id: brandId, category_id: form.category_id, description: form.description, attributes: {},
      variant_axes: Object.fromEntries(parsedAxes.map((a) => [a.name, a.values])),
    })
    if (seller) {
      const proposed = await action.run(() => sellerApi.propose(body(form.brand_id)))
      if (proposed) navigate(`catalog/${proposed.product.id}`)
      return
    }
    const created = await action.run(async () => api.createProduct(body(form.brand_id || (await api.createBrand(form.brand_name)).id)))
    if (created) navigate(`products/${created.id}`)
  }

  return (
    <Loading state={lookups}>
      {([brands, categories]) => (
        <Card title={seller ? 'Propose a new product' : 'Create a product'}>
          {seller && <p className="muted">Can't find your product in the catalog? Propose it here. Our team reviews it, usually within a day, and you can already add your price and stock.</p>}
          <form className="form" onSubmit={submit} data-testid="new-product-form">
            <label className="field wide"><span>Title</span>
              <input required minLength={3} maxLength={200} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} placeholder="e.g. Velora Linen Midi Dress" />
            </label>
            <label className="field"><span>Brand</span>
              <select value={form.brand_id} onChange={(e) => setForm({ ...form, brand_id: Number(e.target.value) })}>
                <option value={0}>{seller ? 'Choose a brand…' : 'New brand…'}</option>
                {brands.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
              </select>
            </label>
            {form.brand_id === 0 && !seller ? (
              <label className="field"><span>New brand name</span>
                <input required maxLength={80} value={form.brand_name} onChange={(e) => setForm({ ...form, brand_name: e.target.value })} />
              </label>
            ) : <div />}
            <label className="field"><span>Category</span>
              <CategorySelect categories={categories} value={form.category_id} onChange={(v) => setForm({ ...form, category_id: v })} />
            </label>
            <div />
            <label className="field wide"><span>Description</span>
              <textarea rows={3} maxLength={5000} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
            </label>
            <div className="field wide">
              <span>Variants</span>
              <p className="muted small">Each axis takes comma-separated values; every combination becomes a variant. Leave empty for a single variant.</p>
              {axes.map((a, i) => (
                <div key={i} className="attribute-row">
                  <input placeholder="Axis, e.g. Color" value={a.name} maxLength={40} onChange={(e) => setAxes(axes.map((x, n) => (n === i ? { ...x, name: e.target.value } : x)))} aria-label="Axis name" />
                  <input placeholder="Values, separated by commas" value={a.values} onChange={(e) => setAxes(axes.map((x, n) => (n === i ? { ...x, values: e.target.value } : x)))} aria-label="Axis values" />
                  <button type="button" className="icon-btn" onClick={() => setAxes(axes.filter((_, n) => n !== i))} aria-label="Remove axis"><TrashIcon /></button>
                </div>
              ))}
              {axes.length < 3 && <button type="button" className="btn btn-ghost btn-sm" onClick={() => setAxes([...axes, { name: '', values: '' }])}>Add axis</button>}
              <p className="muted small" data-testid="variant-count">{variantCount} variant{variantCount === 1 ? '' : 's'} will be created</p>
            </div>
            <div className="form-actions wide">
              <button type="submit" className="btn btn-primary" disabled={action.busy || form.category_id === 0 || (seller && form.brand_id === 0)}>
                {seller ? 'Submit for review' : 'Create as draft'}</button>
              <a className="btn btn-ghost" href={seller ? '#/proposals' : '#/products'}>Cancel</a>
            </div>
            <FormError error={action.error} />
          </form>
        </Card>
      )}
    </Loading>
  )
}
