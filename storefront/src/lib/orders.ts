import { cookies } from 'next/headers'
import type { Cart, OrderDetail, OrderSummary, SavedAddress } from './order-types'

export * from './order-types'

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'

async function forwardCookies() {
  const jar = await cookies()
  return ['rs_session', 'rs_cart'].flatMap((name) => {
    const value = jar.get(name)?.value
    return value ? [`${name}=${value}`] : []
  }).join('; ')
}

async function get<T>(path: string): Promise<T | null> {
  try {
    const res = await fetch(`${apiUrl}/api/v1${path}`, { headers: { cookie: await forwardCookies() }, cache: 'no-store' })
    return res.ok ? ((await res.json()) as T) : null
  } catch {
    return null
  }
}

export const getCart = () => get<Cart>('/cart')
export const getCartCount = async () => (await get<{ item_count: number }>('/cart/summary'))?.item_count ?? 0
export const getAddresses = async () => (await get<{ addresses: SavedAddress[] }>('/account/addresses'))?.addresses ?? []
export const getOrders = async () => (await get<{ orders: OrderSummary[] }>('/orders'))?.orders ?? []
export const getOrder = (number: string) => get<OrderDetail>(`/orders/${encodeURIComponent(number)}`)
