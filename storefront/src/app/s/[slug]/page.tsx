import type { Metadata } from 'next'
import Link from 'next/link'
import { notFound } from 'next/navigation'
import { cache } from 'react'
import { ProductCard } from '@/components/ProductCard'
import { StoreIcon, TruckIcon } from '@/components/icons'
import { getStore } from '@/lib/products'

type Props = { params: Promise<{ slug: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }

const load = cache(getStore)

function pageOf(value: string | string[] | undefined) {
  const n = Number(Array.isArray(value) ? value[0] : value)
  return Number.isInteger(n) && n >= 1 && n <= 1000 ? n : 1
}

export async function generateMetadata({ params, searchParams }: Props): Promise<Metadata> {
  const store = await load((await params).slug, pageOf((await searchParams).page))
  if (!store) return {}
  const s = store.seller
  return {
    title: `${s.name}: official store`,
    description: `Shop ${s.product_count} products from ${s.name}${s.city ? ` in ${s.city}` : ''}, rated ${s.rating.toFixed(1)} out of 5 on ReapSeek.`,
    alternates: { canonical: `/s/${s.slug}` },
  }
}

export default async function StorePage({ params, searchParams }: Props) {
  const page = pageOf((await searchParams).page)
  const store = await load((await params).slug, page)
  if (!store) notFound()
  const s = store.seller
  const pages = Math.max(1, Math.ceil(store.total / store.page_size))
  return (
    <div className="container store-page" data-testid="store-page">
      <section className="store-head">
        <div className="store-avatar" aria-hidden="true">{s.name.charAt(0).toUpperCase()}</div>
        <div className="store-title">
          <h1 data-testid="store-name">{s.name}</h1>
          <p className="muted">{s.city ? `${s.city} · ` : ''}Selling on ReapSeek since {new Date(s.joined_at).getFullYear()}</p>
        </div>
        <dl className="store-stats">
          <div><dt>Seller rating</dt><dd>★ {s.rating.toFixed(1)}</dd></div>
          <div><dt>Products</dt><dd data-testid="store-products">{s.product_count.toLocaleString('en-US')}</dd></div>
          <div><dt><StoreIcon size={14} /> Delivered orders</dt><dd>{s.delivered_packages.toLocaleString('en-US')}</dd></div>
          {s.average_handling_days !== null && <div><dt><TruckIcon size={14} /> Ships in</dt><dd>{s.average_handling_days.toFixed(1)} days</dd></div>}
        </dl>
      </section>
      {store.products.length === 0 ? (
        <p className="muted">This seller has nothing in stock right now.</p>
      ) : (
        <div className="product-grid">
          {store.products.map((p) => <ProductCard key={p.id} product={p} />)}
        </div>
      )}
      {pages > 1 && (
        <nav className="pagination" aria-label="Pages">
          {page > 1 && <Link href={`/s/${s.slug}?page=${page - 1}`}>Previous</Link>}
          <span>Page {page} of {pages}</span>
          {page < pages && <Link href={`/s/${s.slug}?page=${page + 1}`}>Next</Link>}
        </nav>
      )}
    </div>
  )
}
