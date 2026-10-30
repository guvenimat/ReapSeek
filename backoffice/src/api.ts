export type User = { id: number; email: string; name: string; role: string }

export type CatalogStats = {
  active_products: number; draft_products: number; archived_products: number; active_sellers: number; suspended_sellers: number
  offers: number; out_of_stock_products: number; brands: number; categories: number
}
export type Category = { id: number; parent_id: number | null; slug: string; name: string; path: string; depth: number; position: number; image_kind: string; product_count: number }
export type Brand = { id: number; name: string; slug: string; product_count: number }
export type ProductStatus = 'draft' | 'active' | 'archived'
export type ProductListItem = {
  id: number; slug: string; title: string; brand: string; category_name: string; category_path: string; status: ProductStatus
  variant_count: number; seller_count: number; price: number | null; list_price: number | null; image_url: string | null; updated_at: string
}
export type ProductsPage = { products: ProductListItem[]; total: number; page: number; page_size: number }
export type Offer = {
  id: number; seller_id: number; seller_name: string; seller_status: string; seller_rating: number; variant_id: number
  price: number; list_price: number | null; stock: number; handling_days: number; status: 'active' | 'paused'; is_buy_box: boolean; updated_at: string
}
export type Variant = { id: number; sku: string; options: Record<string, string>; position: number; offers: Offer[] }
export type ProductDetail = {
  id: number; slug: string; title: string; brand_id: number; brand: string; category_id: number; category_name: string; category_path: string
  description: string; attributes: Record<string, string>; status: ProductStatus; created_at: string; updated_at: string
  images: { position: number; url: string; alt: string }[]; variants: Variant[]
}
export type Seller = {
  id: number; name: string; slug: string; city: string; status: 'active' | 'suspended'; commission_rate: number; rating: number
  product_count: number; offer_count: number; created_at: string
}
export type OfferInput = { seller_id: number; variant_id: number; price: number; list_price: number | null; stock: number; handling_days: number; status: string }

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? (method === 'POST' ? '{}' : undefined) : JSON.stringify(body),
    credentials: 'same-origin',
  })
  if (res.status === 204) return undefined as T
  const data = await res.json().catch(() => null)
  if (!res.ok) {
    const message = res.status === 429 ? 'Too many attempts. Wait a minute and try again.' : data?.error ?? `Request failed (${res.status})`
    throw new ApiError(res.status, message)
  }
  return data as T
}

const qs = (params: Record<string, string | number | undefined>) => {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== '') p.set(k, String(v))
  return p.toString()
}

const catalog = '/api/v1/backoffice/catalog'

export const api = {
  me: () => request<{ user: User }>('GET', '/api/v1/backoffice/auth/me').then((r) => r.user),
  login: (email: string, password: string) =>
    request<{ user: User }>('POST', '/api/v1/backoffice/auth/login', { email, password }).then((r) => r.user),
  logout: () => request<void>('POST', '/api/v1/backoffice/auth/logout'),
  health: () => request<{ status: string; database: string }>('GET', '/healthz'),

  stats: () => request<CatalogStats>('GET', `${catalog}/stats`),
  categories: () => request<{ categories: Category[] }>('GET', `${catalog}/categories`).then((r) => r.categories),
  createCategory: (body: { parent_id: number | null; name: string; image_kind?: string }) => request<Category>('POST', `${catalog}/categories`, body),
  brands: () => request<{ brands: Brand[] }>('GET', `${catalog}/brands`).then((r) => r.brands),
  createBrand: (name: string) => request<Brand>('POST', `${catalog}/brands`, { name }),
  products: (params: { q?: string; status?: string; category?: string; page?: number }) =>
    request<ProductsPage>('GET', `${catalog}/products?${qs(params)}`),
  product: (id: number) => request<ProductDetail>('GET', `${catalog}/products/${id}`),
  createProduct: (body: { title: string; brand_id: number; category_id: number; description: string; attributes: Record<string, string>; variant_axes: Record<string, string[]> }) =>
    request<ProductDetail>('POST', `${catalog}/products`, body),
  updateProduct: (id: number, body: Partial<{ title: string; brand_id: number; category_id: number; description: string; attributes: Record<string, string>; status: ProductStatus }>) =>
    request<ProductDetail>('PATCH', `${catalog}/products/${id}`, body),
  saveOffer: (body: OfferInput) => request<ProductDetail>('PUT', `${catalog}/offers`, body),
  deleteOffer: (id: number) => request<ProductDetail>('DELETE', `${catalog}/offers/${id}`),

  sellers: () => request<{ sellers: Seller[] }>('GET', '/api/v1/backoffice/sellers').then((r) => r.sellers),
  createSeller: (body: { name: string; city: string; commission_rate: number }) => request<Seller>('POST', '/api/v1/backoffice/sellers', body),
  updateSeller: (id: number, body: Partial<{ name: string; city: string; status: string; commission_rate: number }>) =>
    request<Seller>('PATCH', `/api/v1/backoffice/sellers/${id}`, body),
}
