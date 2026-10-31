export type CategoryNode = { id: number; slug: string; name: string; path: string; depth: number; image_kind: string; children: CategoryNode[] }
export type ProductCard = {
  id: number; slug: string; title: string; brand: string; category_name: string; price: number | null; list_price: number | null
  discount: number; in_stock: boolean; seller_count: number; image_url: string | null; rating?: number | null; review_count?: number
}
export type FacetValue = { value: string; label: string; count: number; selected: boolean }
export type SearchResult = {
  query: string | null; category: string | null; sort: string; products: ProductCard[]; total: number; page: number; page_size: number
  facets: { departments: FacetValue[]; categories: FacetValue[]; brands: FacetValue[]; colors: FacetValue[]; sizes: FacetValue[]; price: { min: number | null; max: number | null } }
  took_ms: number
}
export type Params = Record<string, string | string[] | undefined>

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'

export const searchKeys = ['q', 'category', 'brand', 'color', 'size', 'min_price', 'max_price', 'in_stock', 'sort', 'page', 'page_size'] as const

export async function getCategories(): Promise<CategoryNode[]> {
  try {
    const res = await fetch(`${apiUrl}/api/v1/catalog/categories`, { next: { revalidate: 60 } })
    if (!res.ok) return []
    return ((await res.json()) as { departments: CategoryNode[] }).departments
  } catch {
    return []
  }
}

export function flatten(nodes: CategoryNode[]): CategoryNode[] {
  return nodes.flatMap((n) => [n, ...flatten(n.children)])
}

export async function findCategory(slug: string): Promise<{ category: CategoryNode; trail: CategoryNode[] } | null> {
  const all = flatten(await getCategories())
  const category = all.find((c) => c.slug === slug)
  if (!category) return null
  const segments = category.path.split('/')
  const trail = segments.map((_, i) => all.find((c) => c.path === segments.slice(0, i + 1).join('/'))!).filter(Boolean)
  return { category, trail }
}

export function toQuery(params: Params, overrides: Params = {}): URLSearchParams {
  const merged: Params = { ...params, ...overrides }
  const qs = new URLSearchParams()
  for (const key of searchKeys) {
    const value = merged[key]
    for (const v of Array.isArray(value) ? value : value === undefined ? [] : [value]) if (v !== '') qs.append(key, v)
  }
  return qs
}

export async function search(params: Params): Promise<{ result: SearchResult | null; error: string | null }> {
  try {
    const res = await fetch(`${apiUrl}/api/v1/search?${toQuery(params)}`, { cache: 'no-store' })
    const data = await res.json().catch(() => null)
    if (!res.ok) return { result: null, error: data?.error ?? 'Search is temporarily unavailable.' }
    return { result: data as SearchResult, error: null }
  } catch {
    return { result: null, error: 'Search is temporarily unavailable.' }
  }
}

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'TRY', currencyDisplay: 'narrowSymbol' })
export const formatPrice = (value: number) => money.format(value)
export const formatCount = (value: number) => new Intl.NumberFormat('en-US').format(value)

export function departmentHue(slug: string): number {
  let h = 0
  for (const ch of slug) h = (h * 31 + ch.charCodeAt(0)) % 360
  return h
}
