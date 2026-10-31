import { useState, type FormEvent } from 'react'
import { api } from '../api'
import { PlusIcon } from '../components/icons'
import { Card, Loading, Pagination, StatusBadge } from '../components/ui'
import { formatCount, formatMoney, navigate, useLoad } from '../lib'

export function Products({ query }: { query: URLSearchParams }) {
  const q = query.get('q') ?? ''
  const status = query.get('status') ?? ''
  const category = query.get('category') ?? ''
  const page = Number(query.get('page') ?? 1) || 1
  const [search, setSearch] = useState(q)
  const categories = useLoad(() => api.categories(), [])
  const products = useLoad(() => api.products({ q, status, category, page }), [q, status, category, page])

  const go = (changes: Record<string, string | number | undefined>) =>
    navigate('products', { q, status, category, page: undefined, ...changes })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    go({ q: search.trim() })
  }

  return (
    <Card
      title={products.data ? `${formatCount(products.data.total)} products` : 'Products'}
      actions={<a className="btn btn-primary" href="#/products/new"><PlusIcon /> New product</a>}
    >
      <form className="filters" onSubmit={submit}>
        <input type="search" placeholder="Search by title, brand or ID" value={search} onChange={(e) => setSearch(e.target.value)} aria-label="Search products" />
        <select value={status} onChange={(e) => go({ status: e.target.value })} aria-label="Status">
          <option value="">All statuses</option>
          <option value="pending_review">Pending review</option>
          <option value="active">Active</option>
          <option value="draft">Draft</option>
          <option value="rejected">Rejected</option>
          <option value="archived">Archived</option>
        </select>
        <select value={category} onChange={(e) => go({ category: e.target.value })} aria-label="Category">
          <option value="">All categories</option>
          {categories.data?.map((c) => (
            <option key={c.id} value={c.path}>{c.depth > 0 ? '   ' : ''}{c.name}</option>
          ))}
        </select>
        <button type="submit" className="btn btn-ghost">Search</button>
      </form>
      <Loading state={products}>
        {(data) => (
          <>
            <div className="table-wrap">
              <table className="table" data-testid="products-table">
                <thead>
                  <tr><th>Product</th><th>Category</th><th>Status</th><th className="num">Variants</th><th className="num">Sellers</th><th className="num">Buy box</th></tr>
                </thead>
                <tbody>
                  {data.products.map((p) => (
                    <tr key={p.id} className="clickable" onClick={() => navigate(`products/${p.id}`)}>
                      <td>
                        <div className="product-cell">
                          {p.image_url ? <img src={p.image_url} alt="" width={40} height={50} loading="lazy" /> : <span className="thumb" />}
                          <div>
                            <a href={`#/products/${p.id}`} className="strong">{p.title}</a>
                            <div className="muted small">{p.brand} · #{p.id}</div>
                          </div>
                        </div>
                      </td>
                      <td className="muted">{p.category_name}</td>
                      <td><StatusBadge status={p.status} /></td>
                      <td className="num">{p.variant_count}</td>
                      <td className="num">{p.seller_count}</td>
                      <td className="num">
                        {p.price == null ? <span className="muted">Out of stock</span> : (
                          <>
                            <strong>{formatMoney(p.price)}</strong>
                            {p.list_price != null && <div className="muted small strike">{formatMoney(p.list_price)}</div>}
                          </>
                        )}
                      </td>
                    </tr>
                  ))}
                  {data.products.length === 0 && (
                    <tr><td colSpan={6} className="empty">No products match these filters.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
            <Pagination page={data.page} pageSize={data.page_size} total={data.total} onPage={(p) => go({ page: p })} />
          </>
        )}
      </Loading>
    </Card>
  )
}
