import { cookies } from 'next/headers'
import type { ProductCard } from './catalog'

export type Placement = 'home_popular' | 'home_trending' | 'product_similar' | 'product_bought_together' | 'cart'
export type RecommendedProduct = { product: ProductCard; source: string }
export type Recommendations = { placement: Placement; model: string; served_from: string; items: RecommendedProduct[] }

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'

export async function getRecommendations(placement: Placement, options: { productId?: number; limit?: number } = {}): Promise<Recommendations | null> {
  const qs = new URLSearchParams()
  if (options.productId) qs.set('product_id', String(options.productId))
  if (options.limit) qs.set('limit', String(options.limit))
  const headers: Record<string, string> = {}
  if (placement === 'cart') {
    const jar = await cookies()
    headers.cookie = ['rs_session', 'rs_cart'].flatMap((name) => {
      const value = jar.get(name)?.value
      return value ? [`${name}=${value}`] : []
    }).join('; ')
  }
  try {
    const res = await fetch(`${apiUrl}/api/v1/recommendations/${placement}?${qs}`, { headers, cache: 'no-store' })
    return res.ok ? ((await res.json()) as Recommendations) : null
  } catch {
    return null
  }
}
