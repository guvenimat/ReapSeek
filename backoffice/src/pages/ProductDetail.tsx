import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { adminImages, api, type Brand, type Category, type Offer, type ProductDetail, type ProductStatus, type Seller, type Variant } from '../api'
import { TrashIcon } from '../components/icons'
import { ProductImages } from '../components/ProductImages'
import { Badge, Card, FormError, Loading, StatusBadge } from '../components/ui'
import { formatDate, formatMoney, formatOptions, useAction, useLoad } from '../lib'

export function ProductDetailPage({ id }: { id: number }) {
  const product = useLoad(() => api.product(id), [id])
  const lookups = useLoad(() => Promise.all([api.brands(), api.categories(), api.sellers()]), [])
  const [current, setCurrent] = useState<ProductDetail>()
  const images = useMemo(() => adminImages(id), [id])

  useEffect(() => setCurrent(product.data), [product.data])

  return (
    <Loading state={product}>
      {() => current && (
        <div className="stack" data-testid="product-detail">
          <Header product={current} onChange={setCurrent} />
          <ProductImages images={images} variants={current.variants} onChanged={() => api.product(id).then(setCurrent)} />
          <Loading state={lookups}>
            {([brands, categories, sellers]) => (
              <>
                <Details product={current} brands={brands} categories={categories} onChange={setCurrent} />
                <Offers product={current} sellers={sellers} onChange={setCurrent} />
              </>
            )}
          </Loading>
        </div>
      )}
    </Loading>
  )
}

function Header({ product, onChange }: { product: ProductDetail; onChange: (p: ProductDetail) => void }) {
  const action = useAction()
  const [image, setImage] = useState(0)
  const [note, setNote] = useState('')
  const setStatus = (status: ProductStatus) => action.run(async () => onChange(await api.updateProduct(product.id, { status })))
  const review = (decision: 'approve' | 'reject') => action.run(async () => onChange(await api.reviewProduct(product.id, decision, note || undefined)))
  const img = product.images[Math.min(image, product.images.length - 1)]

  return (
    <Card>
      <div className="product-head">
        <div className="gallery">
          {img && <img src={img.url} alt={img.alt} width={200} height={250} />}
          {product.images.length > 1 && (
            <div className="thumbs">
              {product.images.map((i, n) => (
                <button key={i.position} type="button" className={n === image ? 'active' : ''} onClick={() => setImage(n)} aria-label={i.alt}>
                  <img src={i.url} alt="" width={36} height={45} />
                </button>
              ))}
            </div>
          )}
        </div>
        <div className="product-meta">
          <div className="muted small">{product.brand} · {product.category_name}</div>
          <h2 data-testid="product-title">{product.title}</h2>
          <div className="row">
            <StatusBadge status={product.status} />
            <span className="muted small">#{product.id} · /p/{product.slug}</span>
          </div>
          <div className="muted small">Created {formatDate(product.created_at)} · Updated {formatDate(product.updated_at)}</div>
          {product.status === 'pending_review' ? (
            <div className="review-box" data-testid="product-review">
              <div className="small strong">A seller proposed this product</div>
              <input placeholder="Note for the seller (required to reject)" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} aria-label="Review note" />
              <div className="row">
                <button className="btn btn-primary btn-sm" disabled={action.busy} onClick={() => review('approve')}>Approve</button>
                <button className="btn btn-ghost btn-sm danger" disabled={action.busy || !note.trim()} onClick={() => review('reject')}>Reject</button>
              </div>
            </div>
          ) : (
          <div className="row">
            {product.status !== 'active' && <button className="btn btn-primary btn-sm" disabled={action.busy} onClick={() => setStatus('active')}>Publish</button>}
            {product.status !== 'draft' && <button className="btn btn-ghost btn-sm" disabled={action.busy} onClick={() => setStatus('draft')}>Move to draft</button>}
            {product.status !== 'archived' && <button className="btn btn-ghost btn-sm danger" disabled={action.busy} onClick={() => setStatus('archived')}>Archive</button>}
          </div>
          )}
          <FormError error={action.error} />
        </div>
      </div>
    </Card>
  )
}

function Details({ product, brands, categories, onChange }: { product: ProductDetail; brands: Brand[]; categories: Category[]; onChange: (p: ProductDetail) => void }) {
  const action = useAction()
  const [saved, setSaved] = useState(false)
  const [form, setForm] = useState(() => ({
    title: product.title, brand_id: product.brand_id, category_id: product.category_id, description: product.description,
    attributes: Object.entries(product.attributes),
  }))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSaved(false)
    const updated = await action.run(() => api.updateProduct(product.id, {
      title: form.title, brand_id: form.brand_id, category_id: form.category_id, description: form.description,
      attributes: Object.fromEntries(form.attributes.filter(([k, v]) => k.trim() && v.trim())),
    }))
    if (updated) {
      onChange(updated)
      setSaved(true)
    }
  }
  const setAttribute = (index: number, key: string, value: string) =>
    setForm({ ...form, attributes: form.attributes.map((a, i) => (i === index ? [key, value] : a)) })

  return (
    <Card title="Details">
      <form className="form" onSubmit={submit} data-testid="product-form">
        <label className="field wide"><span>Title</span>
          <input required minLength={3} maxLength={200} value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        </label>
        <label className="field"><span>Brand</span>
          <select value={form.brand_id} onChange={(e) => setForm({ ...form, brand_id: Number(e.target.value) })}>
            {brands.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
          </select>
        </label>
        <label className="field"><span>Category</span>
          <CategorySelect categories={categories} value={form.category_id} onChange={(v) => setForm({ ...form, category_id: v })} />
        </label>
        <label className="field wide"><span>Description</span>
          <textarea rows={4} maxLength={5000} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </label>
        <div className="field wide">
          <span>Attributes</span>
          <div className="attributes">
            {form.attributes.map(([k, v], i) => (
              <div key={i} className="attribute-row">
                <input placeholder="Name" value={k} maxLength={40} onChange={(e) => setAttribute(i, e.target.value, v)} aria-label="Attribute name" />
                <input placeholder="Value" value={v} maxLength={200} onChange={(e) => setAttribute(i, k, e.target.value)} aria-label="Attribute value" />
                <button type="button" className="icon-btn" onClick={() => setForm({ ...form, attributes: form.attributes.filter((_, n) => n !== i) })} aria-label="Remove attribute"><TrashIcon /></button>
              </div>
            ))}
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setForm({ ...form, attributes: [...form.attributes, ['', '']] })}>Add attribute</button>
          </div>
        </div>
        <div className="form-actions wide">
          <button type="submit" className="btn btn-primary" disabled={action.busy}>Save changes</button>
          {saved && <span className="form-success-inline">Saved</span>}
        </div>
        <FormError error={action.error} />
      </form>
    </Card>
  )
}

export function CategorySelect({ categories, value, onChange }: { categories: Category[]; value: number; onChange: (id: number) => void }) {
  const departments = categories.filter((c) => c.depth === 0)
  return (
    <select value={value} onChange={(e) => onChange(Number(e.target.value))} required>
      <option value={0} disabled>Choose a category</option>
      {departments.map((d) => (
        <optgroup key={d.id} label={d.name}>
          {categories.filter((c) => c.path.startsWith(d.path + '/')).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </optgroup>
      ))}
    </select>
  )
}

function Offers({ product, sellers, onChange }: { product: ProductDetail; sellers: Seller[]; onChange: (p: ProductDetail) => void }) {
  const action = useAction()
  const empty = { variant_id: product.variants[0]?.id ?? 0, seller_id: 0, price: '', list_price: '', stock: '10', handling_days: '1' }
  const [form, setForm] = useState(empty)

  const add = async (e: FormEvent) => {
    e.preventDefault()
    const updated = await action.run(() => api.saveOffer({
      variant_id: form.variant_id, seller_id: form.seller_id, price: Number(form.price),
      list_price: form.list_price ? Number(form.list_price) : null, stock: Number(form.stock), handling_days: Number(form.handling_days), status: 'active',
    }))
    if (updated) {
      onChange(updated)
      setForm({ ...empty, variant_id: form.variant_id })
    }
  }

  return (
    <Card title="Variants and offers">
      <div className="table-wrap">
        <table className="table compact" data-testid="offers-table">
          <thead><tr><th>Variant</th><th>Seller</th><th className="num">Price</th><th className="num">List price</th><th className="num">Stock</th><th className="num">Ships in</th><th>Status</th><th /></tr></thead>
          {product.variants.map((v) => (
            <tbody key={v.id} className="variant-group" data-variant={v.id}>
              {v.offers.length === 0 ? (
                <tr>
                  <VariantCell variant={v} rows={1} />
                  <td colSpan={7} className="muted small">No offers yet; this variant cannot be bought.</td>
                </tr>
              ) : v.offers.map((o, n) => <OfferRow key={o.id} offer={o} variantCell={n === 0 ? <VariantCell variant={v} rows={v.offers.length} /> : null} onChange={onChange} />)}
            </tbody>
          ))}
        </table>
      </div>
      <form className="inline-form" onSubmit={add} data-testid="add-offer">
        <div className="form-title">Add or replace an offer</div>
        <div className="row wrap">
          <select value={form.variant_id} onChange={(e) => setForm({ ...form, variant_id: Number(e.target.value) })} aria-label="Variant">
            {product.variants.map((v) => <option key={v.id} value={v.id}>{formatOptions(v.options)}</option>)}
          </select>
          <select value={form.seller_id} onChange={(e) => setForm({ ...form, seller_id: Number(e.target.value) })} aria-label="Seller" required>
            <option value={0} disabled>Seller</option>
            {sellers.map((s) => <option key={s.id} value={s.id}>{s.name}{s.status === 'suspended' ? ' (suspended)' : ''}</option>)}
          </select>
          <input type="number" min="0.01" step="0.01" placeholder="Price" required value={form.price} onChange={(e) => setForm({ ...form, price: e.target.value })} aria-label="Price" />
          <input type="number" min="0.01" step="0.01" placeholder="List price" value={form.list_price} onChange={(e) => setForm({ ...form, list_price: e.target.value })} aria-label="List price" />
          <input type="number" min="0" step="1" placeholder="Stock" required value={form.stock} onChange={(e) => setForm({ ...form, stock: e.target.value })} aria-label="Stock" />
          <input type="number" min="1" max="10" step="1" placeholder="Days" required value={form.handling_days} onChange={(e) => setForm({ ...form, handling_days: e.target.value })} aria-label="Handling days" />
          <button type="submit" className="btn btn-primary" disabled={action.busy}>Save offer</button>
        </div>
        <FormError error={action.error} />
      </form>
    </Card>
  )
}

function VariantCell({ variant, rows }: { variant: Variant; rows: number }) {
  return (
    <td rowSpan={rows} className="variant-cell">
      <div className="strong">{formatOptions(variant.options)}</div>
      <div className="muted small">SKU {variant.sku}</div>
    </td>
  )
}

function OfferRow({ offer, variantCell, onChange }: { offer: Offer; variantCell: ReactNode; onChange: (p: ProductDetail) => void }) {
  const action = useAction()
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState({ price: String(offer.price), list_price: offer.list_price == null ? '' : String(offer.list_price), stock: String(offer.stock) })

  const save = async (status = offer.status) => {
    const updated = await action.run(() => api.saveOffer({
      seller_id: offer.seller_id, variant_id: offer.variant_id, price: Number(form.price),
      list_price: form.list_price ? Number(form.list_price) : null, stock: Number(form.stock), handling_days: offer.handling_days, status,
    }))
    if (updated) {
      onChange(updated)
      setEditing(false)
    }
  }
  const remove = async () => {
    if (!window.confirm(`Remove the offer from ${offer.seller_name}?`)) return
    const updated = await action.run(() => api.deleteOffer(offer.id))
    if (updated) onChange(updated)
  }

  return (
    <tr className={offer.is_buy_box ? 'buy-box' : undefined}>
      {variantCell}
      <td>
        <div className="strong">{offer.seller_name} {offer.is_buy_box && <Badge tone="brand">Buy box</Badge>}</div>
        <div className="muted small">★ {offer.seller_rating.toFixed(1)}{offer.seller_status === 'suspended' && ' · seller suspended'}</div>
        {action.error && <div className="form-error small">{action.error}</div>}
      </td>
      {editing ? (
        <>
          <td className="num"><input className="cell-input" type="number" step="0.01" value={form.price} onChange={(e) => setForm({ ...form, price: e.target.value })} aria-label="Price" /></td>
          <td className="num"><input className="cell-input" type="number" step="0.01" value={form.list_price} onChange={(e) => setForm({ ...form, list_price: e.target.value })} aria-label="List price" /></td>
          <td className="num"><input className="cell-input" type="number" step="1" value={form.stock} onChange={(e) => setForm({ ...form, stock: e.target.value })} aria-label="Stock" /></td>
        </>
      ) : (
        <>
          <td className="num strong">{formatMoney(offer.price)}</td>
          <td className="num muted">{formatMoney(offer.list_price)}</td>
          <td className={`num${offer.stock === 0 ? ' warn-text' : ''}`}>{offer.stock}</td>
        </>
      )}
      <td className="num">{offer.handling_days} d</td>
      <td><StatusBadge status={offer.status} /></td>
      <td className="actions">
        {editing ? (
          <>
            <button className="btn btn-primary btn-sm" disabled={action.busy} onClick={() => save()}>Save</button>
            <button className="btn btn-ghost btn-sm" onClick={() => setEditing(false)}>Cancel</button>
          </>
        ) : (
          <>
            <button className="btn btn-ghost btn-sm" onClick={() => setEditing(true)}>Edit</button>
            <button className="btn btn-ghost btn-sm" disabled={action.busy} onClick={() => save(offer.status === 'active' ? 'paused' : 'active')}>
              {offer.status === 'active' ? 'Pause' : 'Resume'}
            </button>
            <button className="icon-btn" disabled={action.busy} onClick={remove} aria-label="Remove offer"><TrashIcon /></button>
          </>
        )}
      </td>
    </tr>
  )
}
