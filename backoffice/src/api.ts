export type User = { id: number; email: string; name: string; role: 'admin' | 'seller'; seller_id: number | null }

export type CatalogStats = {
  active_products: number; draft_products: number; archived_products: number; active_sellers: number; suspended_sellers: number
  offers: number; out_of_stock_products: number; brands: number; categories: number
}
export type Category = { id: number; parent_id: number | null; slug: string; name: string; path: string; depth: number; position: number; image_kind: string; product_count: number }
export type Brand = { id: number; name: string; slug: string; product_count: number }
export type ProductStatus = 'draft' | 'pending_review' | 'active' | 'rejected' | 'archived'
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
export type OrderSummary = {
  number: string; status: string; total: number; currency: string; item_count: number; package_count: number; image_url: string | null
  placed_at: string; paid_at: string | null; customer_email: string | null
}
export type OrdersPage = { orders: OrderSummary[]; total: number; page: number; page_size: number }
export type OrderDetail = {
  number: string; status: string; placed_at: string; paid_at: string | null; payment_deadline: string; close_reason: string | null
  address: { full_name: string; phone: string; city: string; district: string; line: string; postal_code: string }
  subtotal: number; shipping_total: number; total: number; currency: string; customer_id: number; customer_email: string | null
  packages: { id: string; shipment_status: string | null; seller_id: number; seller_name: string; status: string; subtotal: number; shipping_fee: number; tracking_number: string | null
    lines: { product_id: number; product_title: string; variant_label: string; sku: string; image_url: string | null; unit_price: number; quantity: number; total: number }[] }[]
  payments: { provider: string; status: string; failure_reason: string | null; created_at: string }[]
  returns: { id: string; number: string; seller_name: string; status: string; reason: string; total: number; requested_at: string; rejection_note: string | null; return_tracking: string | null }[]
}
export type SellerAccount = { id: number; name: string; slug: string; city: string; status: string; commission_rate: number; rating: number }
export type SellerOffer = {
  id: number; product_id: number; product_title: string; product_slug: string; product_status: ProductStatus; variant_id: number; sku: string
  variant_label: string; price: number; list_price: number | null; stock: number; handling_days: number; status: 'active' | 'paused'
  buy_box_price: number | null; is_buy_box: boolean; updated_at: string
}
export type SellerProductView = { product: ProductDetail; buy_box: { variant_id: number; price: number | null; mine: boolean }[]; editable: boolean }
export type Proposal = { id: number; slug: string; title: string; brand: string; category_name: string; status: ProductStatus; review_note: string | null; created_at: string }
export type SellerPackage = {
  id: string; order_number: string; status: 'preparing' | 'shipped' | 'delivered'; placed_at: string; paid_at: string | null
  subtotal: number; shipping_fee: number; commission_rate: number; tracking_number: string | null; shipped_at: string | null; delivered_at: string | null
  address: { full_name: string; phone: string; city: string; district: string; line: string; postal_code: string }
  lines: { product_title: string; variant_label: string; sku: string; unit_price: number; quantity: number }[]
}
export type LedgerEntry = { id: number; order_number: string; kind: 'sale' | 'shipping' | 'commission'; amount: number; available_at: string; payout_id: number | null; created_at: string }
export type Payout = { id: number; seller_id: number; seller_name: string; period_end: string; amount: number; entry_count: number; transfer_reference: string | null; created_at: string }
export type Balance = {
  summary: { in_transit: number; pending: number; available: number; paid_out: number; next_payout_at: string }
  entries: LedgerEntry[]; payouts: Payout[]
}
export type SellerReturn = {
  id: string; number: string; order_number: string; status: 'requested' | 'approved' | 'rejected' | 'received' | 'refunded'; reason: string; comment: string | null
  total: number; requested_at: string; rejection_note: string | null; return_tracking: string | null; received_at: string | null; refunded_at: string | null
  customer_name: string; lines: { product_title: string; variant_label: string; sku: string; quantity: number; unit_price: number }[]
}
export type SellerQuestion = {
  id: number; product_id: number; product_title: string; product_slug: string; body: string; author: string; created_at: string; my_answer: string | null; answer_count: number
}
export type ModerationData = {
  reviews: { id: number; product_id: number; product_title: string; rating: number; title: string; body: string; author: string; status: 'visible' | 'hidden'; created_at: string }[]
  questions: { id: number; product_id: number; product_title: string; body: string; author: string; status: 'visible' | 'hidden'; answer_count: number; created_at: string }[]
}
export type Campaign = {
  id: number; name: string; cohort_id: number; cohort_name: string; kind: 'percent' | 'fixed'; value: number; min_subtotal: number; max_discount: number | null
  valid_days: number; subject: string; message: string; status: 'draft' | 'sending' | 'sent' | 'failed'; recipients: number; redeemed: number; revenue: number
  error: string | null; created_at: string; sent_at: string | null
}
export type AudienceCohort = { id: number; name: string; description: string; status: string; member_count: number | null; calculated_at: string | null }
export type SellerUser = { id: number; email: string; name: string; created_at: string }
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

  orders: (params: { status?: string; q?: string; page?: number }) => request<OrdersPage>('GET', `/api/v1/backoffice/orders?${qs(params)}`),
  order: (number: string) => request<OrderDetail>('GET', `/api/v1/backoffice/orders/${encodeURIComponent(number)}`),

  campaigns: () => request<{ campaigns: Campaign[] }>('GET', '/api/v1/backoffice/marketing/campaigns').then((r) => r.campaigns),
  audienceCohorts: () => request<{ cohorts: AudienceCohort[] }>('GET', '/api/v1/backoffice/marketing/cohorts').then((r) => r.cohorts),
  audiencePreview: (cohortId: number) => request<{ cohort_id: number; customers: number }>('POST', '/api/v1/backoffice/marketing/audience', { cohort_id: cohortId }),
  createCampaign: (body: Record<string, unknown>) => request<Campaign>('POST', '/api/v1/backoffice/marketing/campaigns', body),
  sendCampaign: (id: number) => request<Campaign>('POST', `/api/v1/backoffice/marketing/campaigns/${id}/send`),
  moderation: (status?: string) => request<ModerationData>('GET', `/api/v1/backoffice/moderation?${qs({ status })}`),
  moderate: (kind: 'reviews' | 'questions', id: number, status: 'visible' | 'hidden') => request<void>('PATCH', `/api/v1/backoffice/${kind}/${id}`, { status }),
  payouts: () => request<{ payouts: Payout[] }>('GET', '/api/v1/backoffice/payouts').then((r) => r.payouts),
  runPayouts: () => request<{ period_end: string; created: number; transferred: number; transfer_failures: number }>('POST', '/api/v1/backoffice/payouts/run'),
  reviewProduct: (id: number, decision: 'approve' | 'reject', note?: string) =>
    request<ProductDetail>('POST', `/api/v1/backoffice/catalog/products/${id}/review`, { decision, note }),
  sellerUsers: (id: number) => request<{ users: SellerUser[] }>('GET', `/api/v1/backoffice/sellers/${id}/users`).then((r) => r.users),
  createSellerUser: (id: number, body: { email: string; name: string; password: string }) =>
    request<SellerUser>('POST', `/api/v1/backoffice/sellers/${id}/users`, body),

  sellers: () => request<{ sellers: Seller[] }>('GET', '/api/v1/backoffice/sellers').then((r) => r.sellers),
  createSeller: (body: { name: string; city: string; commission_rate: number }) => request<Seller>('POST', '/api/v1/backoffice/sellers', body),
  updateSeller: (id: number, body: Partial<{ name: string; city: string; status: string; commission_rate: number }>) =>
    request<Seller>('PATCH', `/api/v1/backoffice/sellers/${id}`, body),
}

const seller = '/api/v1/seller'

export const sellerApi = {
  account: () => request<SellerAccount>('GET', `${seller}/account`),
  balance: () => request<Balance>('GET', `${seller}/balance`),
  offers: (params: { q?: string; page?: number }) =>
    request<{ offers: SellerOffer[]; total: number; page: number; page_size: number }>('GET', `${seller}/offers?${qs(params)}`),
  saveOffer: (body: Omit<OfferInput, 'seller_id'>) => request<SellerProductView>('PUT', `${seller}/offers`, body),
  products: (params: { q?: string; page?: number }) => request<ProductsPage>('GET', `${seller}/catalog/products?${qs(params)}`),
  product: (id: number) => request<SellerProductView>('GET', `${seller}/catalog/products/${id}`),
  categories: () => request<{ categories: Category[] }>('GET', `${seller}/catalog/categories`).then((r) => r.categories),
  brands: () => request<{ brands: Brand[] }>('GET', `${seller}/catalog/brands`).then((r) => r.brands),
  propose: (body: { title: string; brand_id: number; category_id: number; description: string; attributes: Record<string, string>; variant_axes: Record<string, string[]> }) =>
    request<SellerProductView>('POST', `${seller}/catalog/products`, body),
  proposals: () => request<{ proposals: Proposal[] }>('GET', `${seller}/proposals`).then((r) => r.proposals),
  packages: (params: { status?: string; page?: number }) =>
    request<{ packages: SellerPackage[]; total: number; page: number; page_size: number }>('GET', `${seller}/packages?${qs(params)}`),
  ship: (id: string) => request<SellerPackage>('POST', `${seller}/packages/${id}/ship`),
  questions: (answered?: boolean) =>
    request<{ questions: SellerQuestion[] }>('GET', `${seller}/questions?${qs({ answered: answered === undefined ? undefined : String(answered) })}`).then((r) => r.questions),
  answer: (id: number, body: string) => request<{ questions: SellerQuestion[] }>('PUT', `${seller}/questions/${id}/answer`, { body }),
  returns: (status?: string) => request<{ returns: SellerReturn[] }>('GET', `${seller}/returns?${qs({ status })}`).then((r) => r.returns),
  approveReturn: (id: string) => request<SellerReturn>('POST', `${seller}/returns/${id}/approve`),
  rejectReturn: (id: string, note: string) => request<SellerReturn>('POST', `${seller}/returns/${id}/reject`, { note }),
}
