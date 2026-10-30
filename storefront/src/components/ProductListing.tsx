import Link from 'next/link'
import type { ReactNode } from 'react'
import { formatCount, toQuery, type CategoryNode, type FacetValue, type Params, type SearchResult } from '@/lib/catalog'
import { ProductCard } from './ProductCard'
import { SortSelect } from './SortSelect'

type Props = {
  basePath: string
  params: Params
  result: SearchResult | null
  error: string | null
  heading: ReactNode
  tree: CategoryNode[]
  fixedCategory?: CategoryNode
}

export function ProductListing({ basePath, params, result, error, heading, tree, fixedCategory }: Props) {
  const href = (overrides: Params) => {
    const qs = toQuery(params, { page: undefined, ...overrides })
    if (fixedCategory) qs.delete('category')
    const s = qs.toString()
    return `${basePath}${s ? `?${s}` : ''}`
  }
  const toggle = (key: string, value: string) => {
    const current = ([] as string[]).concat(params[key] ?? [])
    return href({ [key]: current.includes(value) ? current.filter((v) => v !== value) : [...current, value] })
  }
  const active = ['brand', 'color', 'size', 'min_price', 'max_price', 'in_stock'].some((k) => params[k] !== undefined) || (!fixedCategory && params.category !== undefined)

  return (
    <div className="container listing">
      <div className="listing-head">{heading}</div>
      <div className="listing-body">
        <input type="checkbox" id="show-filters" className="filters-toggle" />
        <label htmlFor="show-filters" className="btn btn-ghost filters-button">Filters{active ? ' (active)' : ''}</label>
        <aside className="facets" aria-label="Filters" data-testid="facets">
          {result && <CategoryFacet result={result} tree={tree} fixedCategory={fixedCategory} href={href} />}
          {result && <Facet title="Brand" values={top(result.facets.brands, 12)} link={(v) => toggle('brand', v)} testId="facet-brand" />}
          {result && <Facet title="Color" values={result.facets.colors} link={(v) => toggle('color', v)} testId="facet-color" swatches />}
          {result && result.facets.sizes.length > 0 && <Facet title="Size" values={result.facets.sizes} link={(v) => toggle('size', v)} testId="facet-size" chips />}
          {result && (
            <form className="facet price-filter" action={basePath} method="get">
              <h3>Price</h3>
              {[...toQuery(params, { min_price: undefined, max_price: undefined, page: undefined, ...(fixedCategory ? { category: undefined } : {}) })].map(([k, v], i) => (
                <input key={`${k}-${i}`} type="hidden" name={k} value={v} />
              ))}
              <div className="price-inputs">
                <input type="number" name="min_price" min={0} placeholder={result.facets.price.min !== null ? String(Math.floor(result.facets.price.min)) : 'Min'} defaultValue={first(params.min_price)} aria-label="Minimum price" />
                <span>–</span>
                <input type="number" name="max_price" min={0} placeholder={result.facets.price.max !== null ? String(Math.ceil(result.facets.price.max)) : 'Max'} defaultValue={first(params.max_price)} aria-label="Maximum price" />
                <button type="submit" className="btn btn-ghost btn-sm">Go</button>
              </div>
            </form>
          )}
          {result && (
            <div className="facet">
              <Link href={href({ in_stock: params.in_stock ? undefined : 'true' })} className={`facet-option check${params.in_stock ? ' selected' : ''}`}>
                <span className="box" aria-hidden="true" />In stock only
              </Link>
            </div>
          )}
        </aside>
        <section className="results">
          <div className="toolbar">
            <p data-testid="result-count">
              {result ? <><strong>{formatCount(result.total)}</strong> {result.total === 1 ? 'product' : 'products'}</> : ' '}
              {active && <Link href={fixedCategory ? basePath + (params.q ? `?q=${encodeURIComponent(first(params.q)!)}` : '') : href({ brand: undefined, color: undefined, size: undefined, min_price: undefined, max_price: undefined, in_stock: undefined, category: undefined })} className="clear-filters">Clear filters</Link>}
            </p>
            <SortSelect value={result?.sort ?? 'relevance'} hasQuery={!!params.q} />
          </div>
          {error && <div className="empty-state"><strong>Something went wrong</strong><span>{error}</span></div>}
          {result && result.products.length === 0 && (
            <div className="empty-state" data-testid="no-results">
              <strong>No products found{result.query ? ` for "${result.query}"` : ''}</strong>
              <span>Try a different spelling, fewer words or fewer filters.</span>
            </div>
          )}
          {result && result.products.length > 0 && (
            <div className="product-grid" data-testid="product-grid">
              {result.products.map((p) => <ProductCard key={p.id} product={p} />)}
            </div>
          )}
          {result && <Pagination result={result} href={(page) => href({ page: page > 1 ? String(page) : undefined })} />}
        </section>
      </div>
    </div>
  )
}

function CategoryFacet({ result, tree, fixedCategory, href }: { result: SearchResult; tree: CategoryNode[]; fixedCategory?: CategoryNode; href: (o: Params) => string }) {
  const selected = fixedCategory?.path ?? result.category
  const counts = new Map([...result.facets.departments, ...result.facets.categories].map((f) => [f.value, f.count]))
  const link = (node: CategoryNode) => (fixedCategory ? `/c/${node.slug}${href({}).replace(/^[^?]*/, '')}` : href({ category: node.path }))
  const department = selected ? tree.find((d) => d.path === selected.split('/')[0]) : undefined
  const items = department ? department.children : tree
  return (
    <div className="facet" data-testid="facet-category">
      <h3>Category</h3>
      {department && (
        <Link href={fixedCategory ? `/c/${department.slug}` : href({ category: department.path })} className={`facet-option parent${selected === department.path ? ' selected' : ''}`}>
          {department.name}
        </Link>
      )}
      {items.filter((n) => (counts.get(n.path) ?? 0) > 0 || n.path === selected).map((n) => (
        <Link key={n.path} href={link(n)} className={`facet-option${department ? ' child' : ''}${n.path === selected ? ' selected' : ''}`}>
          <span>{n.name}</span>
          <span className="count">{counts.get(n.path) ?? 0}</span>
        </Link>
      ))}
    </div>
  )
}

function Facet({ title, values, link, testId, swatches, chips }: { title: string; values: FacetValue[]; link: (v: string) => string; testId: string; swatches?: boolean; chips?: boolean }) {
  if (values.length === 0) return null
  return (
    <div className="facet" data-testid={testId}>
      <h3>{title}</h3>
      <div className={chips ? 'chips' : 'facet-list'}>
        {values.map((v) => (
          <Link key={v.value} href={link(v.value)} rel="nofollow" className={`facet-option${chips ? ' chip' : ' check'}${v.selected ? ' selected' : ''}`} data-value={v.value}>
            {!chips && <span className="box" aria-hidden="true" />}
            {swatches && <span className={`swatch swatch-${v.value.toLowerCase()}`} aria-hidden="true" />}
            <span>{v.label}</span>
            {!chips && <span className="count">{v.count}</span>}
          </Link>
        ))}
      </div>
    </div>
  )
}

function Pagination({ result, href }: { result: SearchResult; href: (page: number) => string }) {
  const pages = Math.min(Math.ceil(result.total / result.page_size), 100)
  if (pages <= 1) return null
  const around = [...new Set([1, result.page - 1, result.page, result.page + 1, pages])].filter((p) => p >= 1 && p <= pages).sort((a, b) => a - b)
  return (
    <nav className="pagination" aria-label="Pages" data-testid="pagination">
      {result.page > 1 && <Link href={href(result.page - 1)} rel="prev">Previous</Link>}
      {around.map((p, i) => (
        <span key={p} className="page-group">
          {i > 0 && p - around[i - 1] > 1 && <span className="gap">…</span>}
          {p === result.page ? <span className="current" aria-current="page">{p}</span> : <Link href={href(p)}>{p}</Link>}
        </span>
      ))}
      {result.page < pages && <Link href={href(result.page + 1)} rel="next">Next</Link>}
    </nav>
  )
}

const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v)

const top = (values: FacetValue[], n: number) => values.filter((v, i) => i < n || v.selected)
