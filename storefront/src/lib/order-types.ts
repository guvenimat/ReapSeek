export type CartLine = {
  offer_id: number; product_id: number; product_slug: string; product_title: string; brand: string; variant_label: string; image_url: string | null
  quantity: number; unit_price: number; list_price: number | null; added_price: number; line_total: number; available: number; handling_days: number
  status: 'ok' | 'price_changed' | 'low_stock' | 'unavailable'; message: string | null
}
export type CartPackage = { seller_id: number; seller_name: string; seller_rating: number; lines: CartLine[]; subtotal: number; shipping_fee: number; free_shipping_remaining: number }
export type Cart = {
  packages: CartPackage[]; item_count: number; subtotal: number; shipping_total: number; total: number; currency: string; can_checkout: boolean; warnings: string[]
}
export type SavedAddress = { id: number; full_name: string; phone: string; city: string; district: string; line: string; postal_code: string }
export type OrderSummary = {
  number: string; status: string; total: number; currency: string; item_count: number; package_count: number; image_url: string | null
  placed_at: string; paid_at: string | null
}
export type OrderLine = { product_id: number; product_title: string; variant_label: string; sku: string; image_url: string | null; unit_price: number; quantity: number; total: number }
export type OrderPackage = { seller_id: number; seller_name: string; status: string; subtotal: number; shipping_fee: number; tracking_number: string | null; lines: OrderLine[] }
export type OrderDetail = {
  number: string; status: string; placed_at: string; paid_at: string | null; payment_deadline: string; close_reason: string | null
  address: { full_name: string; phone: string; city: string; district: string; line: string; postal_code: string }
  subtotal: number; shipping_total: number; total: number; currency: string; packages: OrderPackage[]
  payments: { provider: string; status: string; failure_reason: string | null; created_at: string }[]; payment_url: string | null
}

export const orderStatusLabel: Record<string, string> = {
  pending_payment: 'Awaiting payment',
  paid: 'Paid',
  payment_failed: 'Payment failed',
  cancelled: 'Cancelled',
  awaiting_payment: 'Awaiting payment',
  preparing: 'Preparing',
  shipped: 'Shipped',
  delivered: 'Delivered',
}

export const failureLabel: Record<string, string> = {
  card_declined: 'Your card was declined.',
  insufficient_funds: 'Your card has insufficient funds.',
  authentication_failed: 'Your bank could not verify the payment.',
  authentication_cancelled: 'You cancelled the payment.',
  payment_timeout: 'The payment was not completed in time.',
  payment_provider_unavailable: 'Payments were temporarily unavailable.',
}
