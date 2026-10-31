export type StoreOffer = {
  id: number; seller_id: number; seller_name: string; seller_slug: string; seller_rating: number; seller_city: string
  price: number; list_price: number | null; discount: number; low_stock: number | null; handling_days: number; is_buy_box: boolean
}
export type StoreVariant = { id: number; sku: string; options: Record<string, string>; offers: StoreOffer[] }
export type StoreProduct = {
  id: number; slug: string; title: string; brand: string; brand_slug: string; description: string; attributes: Record<string, string>
  categories: { name: string; slug: string; path: string }[]; images: { url: string; alt: string; color: string | null }[]
  axes: { name: string; values: string[] }[]; variants: StoreVariant[]
  min_price: number | null; max_price: number | null; seller_count: number; updated_at: string
}

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'

export const publicUrl = (process.env.PUBLIC_URL ?? 'http://localhost:3100').replace(/\/$/, '')

export async function getProduct(slug: string): Promise<StoreProduct | null> {
  const res = await fetch(`${apiUrl}/api/v1/catalog/products/${encodeURIComponent(slug)}`, { cache: 'no-store' })
  if (res.status === 404) return null
  if (!res.ok) throw new Error(`Product request failed with ${res.status}`)
  return (await res.json()) as StoreProduct
}

export async function getSitemap(): Promise<{ products: { slug: string; updated_at: string }[]; categories: string[] }> {
  const res = await fetch(`${apiUrl}/api/v1/catalog/sitemap`, { cache: 'no-store' })
  if (!res.ok) return { products: [], categories: [] }
  return res.json()
}

export function initialVariant(product: StoreProduct, requested?: number): StoreVariant {
  const byId = product.variants.find((v) => v.id === requested)
  if (byId) return byId
  const available = product.variants.filter((v) => v.offers.length > 0)
  if (available.length === 0) return product.variants[0]
  return available.reduce((best, v) => (buyBox(v)!.price < buyBox(best)!.price ? v : best))
}

export function buyBox(variant: StoreVariant): StoreOffer | undefined {
  return variant.offers.find((o) => o.is_buy_box) ?? variant.offers[0]
}

export type StoreSeller = {
  id: number; name: string; slug: string; city: string; rating: number; joined_at: string; product_count: number
  delivered_packages: number; average_handling_days: number | null
}
export type StorePage = { seller: StoreSeller; products: import('./catalog').ProductCard[]; total: number; page: number; page_size: number }

export async function getStore(slug: string, page: number): Promise<StorePage | null> {
  const res = await fetch(`${apiUrl}/api/v1/catalog/sellers/${encodeURIComponent(slug)}?page=${page}`, { cache: 'no-store' })
  if (res.status === 404) return null
  if (!res.ok) throw new Error(`Store request failed with ${res.status}`)
  return (await res.json()) as StorePage
}
