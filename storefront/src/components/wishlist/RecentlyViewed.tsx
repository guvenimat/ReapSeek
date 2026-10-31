'use client'

import { useEffect, useState } from 'react'
import { api } from '@/lib/client'
import type { ProductCard as Card } from '@/lib/catalog'
import { recentlyViewed, rememberViewed } from '@/lib/recent'
import { ProductCard } from '../ProductCard'

export function RecentlyViewed({ current }: { current?: number }) {
  const [products, setProducts] = useState<Card[]>([])

  useEffect(() => {
    const ids = recentlyViewed().filter((id) => id !== current)
    if (current) rememberViewed(current)
    if (ids.length === 0) return
    api<{ products: Card[] }>('GET', `/catalog/cards?ids=${ids.join(',')}`).then((r) => setProducts(r.products)).catch(() => {})
  }, [current])

  if (products.length === 0) return null
  return (
    <section className="section" data-testid="recently-viewed">
      <div className="section-head">
        <h2>Recently viewed</h2>
      </div>
      <div className="shelf">
        {products.map((p) => <ProductCard key={p.id} product={p} />)}
      </div>
    </section>
  )
}
