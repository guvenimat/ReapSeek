import { useEffect, useState, type FormEvent } from 'react'
import { sellerApi, type SellerPackage, type SellerProductView, type User, type Variant } from '../api'
import { PlusIcon } from '../components/icons'
import { Badge, Card, FormError, Loading, Pagination, Stat, StatusBadge } from '../components/ui'
import { OrderStatusBadge } from './Orders'
import { formatCount, formatDate, formatMoney, formatOptions, navigate, plural, useAction, useLoad } from '../lib'

const kindLabels: Record<string, string> = { sale: 'Sale', shipping: 'Shipping fee', commission: 'Commission' }

export function SellerHome({ user }: { user: User }) {
  const account = useLoad(() => sellerApi.account(), [])
  const balance = useLoad(() => sellerApi.balance(), [])
  const waiting = useLoad(() => sellerApi.packages({ status: 'preparing' }), [])

  return (
    <div className="stack">
      <Card title={account.data ? account.data.name : `Welcome, ${user.name}`}>
        <Loading state={account}>
          {(a) => (
            <p className="muted">
              {a.city || 'No city'} · ★ {a.rating.toFixed(1)} · Commission {(a.commission_rate * 100).toFixed(1)}% · <StatusBadge status={a.status} />
            </p>
          )}
        </Loading>
        <Loading state={waiting}>
          {(w) => w.total > 0
            ? <p><a className="btn btn-primary btn-sm" href="#/packages?status=preparing">{plural(w.total, 'package')} to ship</a></p>
            : <p className="muted small">No packages are waiting to be shipped.</p>}
        </Loading>
      </Card>
      <Loading state={balance}>
        {(b) => (
          <div className="stack">
            <div className="stats" data-testid="seller-balance">
              <Stat label="In transit" value={formatMoney(b.summary.in_transit)} />
              <Stat label="Pending (7-day hold)" value={formatMoney(b.summary.pending)} />
              <Stat label="Available for payout" value={formatMoney(b.summary.available)} testId="stat-available" />
              <Stat label="Paid out" value={formatMoney(b.summary.paid_out)} />
            </div>
            <p className="muted small">In transit: paid orders not delivered yet, after commission. Pending: delivered, paid out 7 days after delivery.
              Next payout: {formatDate(b.summary.next_payout_at)}.</p>
            <Card title="Payouts" testId="seller-payouts">
              <table className="table compact">
                <thead><tr><th>Period ends</th><th className="num">Entries</th><th className="num">Amount</th><th>Transfer</th></tr></thead>
                <tbody>
                  {b.payouts.map((p) => (
                    <tr key={p.id}><td>{formatDate(p.period_end)}</td><td className="num">{p.entry_count}</td><td className="num strong">{formatMoney(p.amount)}</td>
                      <td className="muted">{p.transfer_reference ?? 'pending'}</td></tr>
                  ))}
                  {b.payouts.length === 0 && <tr><td colSpan={4} className="empty">No payouts yet. Payouts run every Monday.</td></tr>}
                </tbody>
              </table>
            </Card>
            <Card title="Ledger" testId="seller-ledger">
              <table className="table compact">
                <thead><tr><th>Order</th><th>Entry</th><th className="num">Amount</th><th>Available</th><th>Status</th></tr></thead>
                <tbody>
                  {b.entries.map((e) => (
                    <tr key={e.id}><td className="strong">{e.order_number}</td><td>{kindLabels[e.kind] ?? e.kind}</td>
                      <td className={`num${e.amount < 0 ? ' negative' : ''}`}>{formatMoney(e.amount)}</td><td className="muted">{formatDate(e.available_at)}</td>
                      <td>{e.payout_id ? <Badge tone="good">paid out</Badge> : new Date(e.available_at) <= new Date() ? <Badge tone="brand">available</Badge> : <Badge tone="warn">pending</Badge>}</td></tr>
                  ))}
                  {b.entries.length === 0 && <tr><td colSpan={5} className="empty">Entries appear when a package is delivered.</td></tr>}
                </tbody>
              </table>
            </Card>
          </div>
        )}
      </Loading>
    </div>
  )
}

export function SellerPackages({ query }: { query: URLSearchParams }) {
  const status = query.get('status') ?? ''
  const page = Number(query.get('page') ?? 1)
  const packages = useLoad(() => sellerApi.packages({ status, page }), [status, page])
  const go = (changes: Record<string, string | number | undefined>) => navigate('packages', { status, page: undefined, ...changes })

  return (
    <Card
      title={packages.data ? plural(packages.data.total, 'package') : 'Packages'}
      actions={
        <select value={status} onChange={(e) => go({ status: e.target.value })} aria-label="Status">
          <option value="">All</option>
          <option value="preparing">To ship</option>
          <option value="shipped">Shipped</option>
          <option value="delivered">Delivered</option>
        </select>
      }
    >
      <Loading state={packages}>
        {(data) => (
          <div className="stack" data-testid="seller-packages">
            {data.packages.map((p) => <PackageCard key={p.id} pkg={p} onShipped={packages.reload} />)}
            {data.packages.length === 0 && <p className="empty">No packages here.</p>}
            <Pagination page={data.page} pageSize={data.page_size} total={data.total} onPage={(n) => go({ page: n })} />
          </div>
        )}
      </Loading>
    </Card>
  )
}

function PackageCard({ pkg, onShipped }: { pkg: SellerPackage; onShipped: () => void }) {
  const action = useAction()
  const ship = async () => {
    if (await action.run(() => sellerApi.ship(pkg.id))) onShipped()
  }
  const commission = Math.round(pkg.subtotal * pkg.commission_rate * 100) / 100
  return (
    <div className="package" data-package={pkg.id}>
      <div className="package-head">
        <div>
          <strong>{pkg.order_number}</strong> <OrderStatusBadge status={pkg.status} />
          <div className="muted small">Paid {pkg.paid_at ? formatDate(pkg.paid_at) : '-'}{pkg.tracking_number && <> · Tracking <strong>{pkg.tracking_number}</strong></>}</div>
        </div>
        {pkg.status === 'preparing' && <button className="btn btn-primary btn-sm" disabled={action.busy} onClick={ship}>Ship with Sandbox Cargo</button>}
      </div>
      <div className="package-body">
        <ul className="plain-list">
          {pkg.lines.map((l) => <li key={l.sku}>{l.quantity} × {l.product_title} <span className="muted">{l.variant_label} · {l.sku}</span></li>)}
        </ul>
        <address className="muted small">
          {pkg.address.full_name}, {pkg.address.phone}<br />{pkg.address.line}, {pkg.address.district}/{pkg.address.city} {pkg.address.postal_code}
        </address>
        <div className="small">
          Items {formatMoney(pkg.subtotal)} · Shipping {pkg.shipping_fee === 0 ? 'free' : formatMoney(pkg.shipping_fee)} · Commission −{formatMoney(commission)}
          <div className="strong">You earn {formatMoney(pkg.subtotal + pkg.shipping_fee - commission)}</div>
        </div>
      </div>
      <FormError error={action.error} />
    </div>
  )
}

export function SellerOffers({ query }: { query: URLSearchParams }) {
  const q = query.get('q') ?? ''
  const page = Number(query.get('page') ?? 1)
  const [search, setSearch] = useState(q)
  const offers = useLoad(() => sellerApi.offers({ q, page }), [q, page])
  const submit = (e: FormEvent) => {
    e.preventDefault()
    navigate('offers', { q: search })
  }

  return (
    <Card title={offers.data ? plural(offers.data.total, 'offer') : 'Offers'}
      actions={<a className="btn btn-primary" href="#/catalog"><PlusIcon /> Add an offer</a>}>
      <form className="filters" onSubmit={submit}>
        <input type="search" placeholder="Search by product or SKU" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search offers" />
      </form>
      <Loading state={offers}>
        {(data) => (
          <>
            <div className="table-wrap">
              <table className="table" data-testid="seller-offers">
                <thead><tr><th>Product</th><th>Variant</th><th className="num">Price</th><th className="num">Buy box</th><th className="num">Stock</th><th>Status</th></tr></thead>
                <tbody>
                  {data.offers.map((o) => (
                    <tr key={o.id} className="clickable" onClick={() => navigate(`catalog/${o.product_id}`)}>
                      <td><div className="strong">{o.product_title}</div><div className="muted small">{o.sku}{o.product_status !== 'active' && <> · <StatusBadge status={o.product_status} /></>}</div></td>
                      <td>{o.variant_label || 'Default'}</td>
                      <td className="num strong">{formatMoney(o.price)}</td>
                      <td className="num">{o.is_buy_box ? <Badge tone="good">You win</Badge> : o.buy_box_price == null ? '-' : formatMoney(o.buy_box_price)}</td>
                      <td className={`num${o.stock === 0 ? ' negative' : ''}`}>{formatCount(o.stock)}</td>
                      <td><StatusBadge status={o.status} /></td>
                    </tr>
                  ))}
                  {data.offers.length === 0 && <tr><td colSpan={6} className="empty">No offers yet. Find a product in the catalog and add your price.</td></tr>}
                </tbody>
              </table>
            </div>
            <Pagination page={data.page} pageSize={data.page_size} total={data.total} onPage={(n) => navigate('offers', { q, page: n })} />
          </>
        )}
      </Loading>
    </Card>
  )
}

export function SellerCatalog({ query }: { query: URLSearchParams }) {
  const q = query.get('q') ?? ''
  const page = Number(query.get('page') ?? 1)
  const [search, setSearch] = useState(q)
  const products = useLoad(() => sellerApi.products({ q, page }), [q, page])
  const submit = (e: FormEvent) => {
    e.preventDefault()
    navigate('catalog', { q: search })
  }
  return (
    <Card title="Find a product to sell" actions={<a className="btn btn-ghost" href="#/catalog/new">Propose a new product</a>}>
      <form className="filters" onSubmit={submit}>
        <input type="search" placeholder="Search the catalog" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search the catalog" />
      </form>
      <Loading state={products}>
        {(data) => (
          <>
            <div className="table-wrap">
              <table className="table" data-testid="seller-catalog">
                <thead><tr><th>Product</th><th>Category</th><th className="num">Sellers</th><th className="num">From</th></tr></thead>
                <tbody>
                  {data.products.map((p) => (
                    <tr key={p.id} className="clickable" onClick={() => navigate(`catalog/${p.id}`)}>
                      <td><div className="product-cell">{p.image_url && <img src={p.image_url} alt="" width={40} height={50} />}<div><div className="strong">{p.title}</div><div className="muted small">{p.brand}</div></div></div></td>
                      <td className="muted">{p.category_name}</td>
                      <td className="num">{p.seller_count}</td>
                      <td className="num">{formatMoney(p.price)}</td>
                    </tr>
                  ))}
                  {data.products.length === 0 && <tr><td colSpan={4} className="empty">No product matches. You can propose it.</td></tr>}
                </tbody>
              </table>
            </div>
            <Pagination page={data.page} pageSize={data.page_size} total={data.total} onPage={(n) => navigate('catalog', { q, page: n })} />
          </>
        )}
      </Loading>
    </Card>
  )
}

export function SellerProduct({ id }: { id: number }) {
  const loaded = useLoad(() => sellerApi.product(id), [id])
  const [view, setView] = useState<SellerProductView>()
  useEffect(() => setView(loaded.data), [loaded.data])

  return (
    <Loading state={loaded}>
      {() => view && (
        <div className="stack" data-testid="seller-product">
          <Card>
            <div className="product-head">
              <div className="gallery">{view.product.images[0] && <img src={view.product.images[0].url} alt={view.product.images[0].alt} width={160} height={200} />}</div>
              <div className="product-meta">
                <div className="muted small">{view.product.brand} · {view.product.category_name}</div>
                <h2>{view.product.title}</h2>
                <div className="row"><StatusBadge status={view.product.status} /><span className="muted small">#{view.product.id}</span></div>
                {view.product.status === 'pending_review' && <p className="muted small">Waiting for review. Your offer goes live when the product is approved.</p>}
              </div>
            </div>
          </Card>
          <Card title="Your price and stock per variant">
            <table className="table" data-testid="seller-variants">
              <thead><tr><th>Variant</th><th className="num">Buy box</th><th className="num">Price</th><th className="num">List price</th><th className="num">Stock</th><th className="num">Days to ship</th><th>Status</th><th /></tr></thead>
              <tbody>
                {view.product.variants.map((v) => (
                  <VariantOffer key={v.id} variant={v} buyBox={view.buy_box.find((b) => b.variant_id === v.id)} onSaved={setView} />
                ))}
              </tbody>
            </table>
          </Card>
        </div>
      )}
    </Loading>
  )
}

function VariantOffer({ variant, buyBox, onSaved }: {
  variant: Variant; buyBox?: { price: number | null; mine: boolean }; onSaved: (v: SellerProductView) => void
}) {
  const mine = variant.offers[0]
  const action = useAction()
  const [form, setForm] = useState({
    price: mine ? String(mine.price) : '', list_price: mine?.list_price != null ? String(mine.list_price) : '',
    stock: mine ? String(mine.stock) : '0', handling_days: mine ? String(mine.handling_days) : '1', status: mine?.status ?? 'active',
  })
  const save = async (e: FormEvent) => {
    e.preventDefault()
    const saved = await action.run(() => sellerApi.saveOffer({
      variant_id: variant.id, price: Number(form.price), list_price: form.list_price ? Number(form.list_price) : null,
      stock: Number(form.stock), handling_days: Number(form.handling_days), status: form.status,
    }))
    if (saved) onSaved(saved)
  }
  const formId = `offer-${variant.id}`
  return (
    <tr data-variant={variant.id}>
      <td><div className="strong">{formatOptions(variant.options)}</div><div className="muted small">{variant.sku}</div><FormError error={action.error} /></td>
      <td className="num">{buyBox?.mine ? <Badge tone="good">You win</Badge> : formatMoney(buyBox?.price)}</td>
      <td className="num"><input form={formId} className="num-input" type="number" min="0.01" step="0.01" required value={form.price} onChange={(e) => setForm({ ...form, price: e.target.value })} aria-label="Price" /></td>
      <td className="num"><input form={formId} className="num-input" type="number" min="0.01" step="0.01" value={form.list_price} onChange={(e) => setForm({ ...form, list_price: e.target.value })} aria-label="List price" /></td>
      <td className="num"><input form={formId} className="num-input" type="number" min="0" step="1" required value={form.stock} onChange={(e) => setForm({ ...form, stock: e.target.value })} aria-label="Stock" /></td>
      <td className="num"><input form={formId} className="num-input" type="number" min="1" max="10" step="1" required value={form.handling_days} onChange={(e) => setForm({ ...form, handling_days: e.target.value })} aria-label="Days to ship" /></td>
      <td>
        <select form={formId} value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as 'active' | 'paused' })} aria-label="Offer status">
          <option value="active">Active</option>
          <option value="paused">Paused</option>
        </select>
      </td>
      <td className="actions">
        <form id={formId} onSubmit={save}><button type="submit" className="btn btn-primary btn-sm" disabled={action.busy}>{mine ? 'Save' : 'Add offer'}</button></form>
      </td>
    </tr>
  )
}

export function SellerProposals() {
  const proposals = useLoad(() => sellerApi.proposals(), [])
  return (
    <Card title="Your product proposals" actions={<a className="btn btn-primary" href="#/catalog/new"><PlusIcon /> Propose a product</a>}>
      <Loading state={proposals}>
        {(list) => (
          <table className="table" data-testid="seller-proposals">
            <thead><tr><th>Product</th><th>Category</th><th>Status</th><th>Note from ReapSeek</th><th>Submitted</th></tr></thead>
            <tbody>
              {list.map((p) => (
                <tr key={p.id} className="clickable" onClick={() => navigate(`catalog/${p.id}`)}>
                  <td><div className="strong">{p.title}</div><div className="muted small">{p.brand}</div></td>
                  <td className="muted">{p.category_name}</td>
                  <td><StatusBadge status={p.status} /></td>
                  <td>{p.review_note ?? <span className="muted">-</span>}</td>
                  <td className="muted">{formatDate(p.created_at)}</td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={5} className="empty">You have not proposed any products.</td></tr>}
            </tbody>
          </table>
        )}
      </Loading>
    </Card>
  )
}
