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
export type OrderLine = {
  id: string; product_id: number; product_title: string; variant_label: string; sku: string; image_url: string | null; unit_price: number; quantity: number; total: number
  returnable_quantity: number
}
export type TrackingEvent = { kind: 'outbound' | 'return'; status: string; occurred_at: string }
export type OrderPackage = {
  id: string; seller_id: number; seller_name: string; status: string; subtotal: number; shipping_fee: number; tracking_number: string | null; lines: OrderLine[]
  shipment_status: string | null; shipped_at: string | null; delivered_at: string | null; return_deadline: string | null; returnable: boolean; tracking: TrackingEvent[]
}
export type OrderReturn = {
  id: string; number: string; package_id: string; seller_name: string; status: string; reason: string; comment: string | null; total: number; requested_at: string
  rejection_note: string | null; return_tracking: string | null; received_at: string | null; refunded_at: string | null
  lines: { order_line_id: string; product_title: string; variant_label: string; quantity: number; total: number }[]; tracking: TrackingEvent[]
}
export type OrderDetail = {
  number: string; status: string; placed_at: string; paid_at: string | null; payment_deadline: string; close_reason: string | null
  address: { full_name: string; phone: string; city: string; district: string; line: string; postal_code: string }
  subtotal: number; shipping_total: number; total: number; currency: string; packages: OrderPackage[]
  payments: { provider: string; status: string; failure_reason: string | null; created_at: string }[]; payment_url: string | null; returns: OrderReturn[]
  discount_total: number; coupon_code: string | null; cancellable: boolean; refunded_at: string | null
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

export const shipmentSteps = ['label_created', 'in_transit', 'out_for_delivery', 'delivered'] as const
export const shipmentLabel: Record<string, string> = {
  label_created: 'Handed to the carrier', in_transit: 'In transit', out_for_delivery: 'Out for delivery', delivered: 'Delivered',
}
export const returnStatusLabel: Record<string, string> = {
  requested: 'Waiting for the seller', approved: 'Approved, send it back', rejected: 'Not accepted', received: 'Received, refunding', refunded: 'Refunded',
}
export const returnReasons: Record<string, string> = {
  changed_mind: 'I changed my mind', wrong_size: 'Wrong size', damaged: 'Arrived damaged', not_as_described: 'Not as described', wrong_item: 'Wrong item sent',
}

export const failureLabel: Record<string, string> = {
  card_declined: 'Your card was declined.',
  insufficient_funds: 'Your card has insufficient funds.',
  authentication_failed: 'Your bank could not verify the payment.',
  authentication_cancelled: 'You cancelled the payment.',
  payment_timeout: 'The payment was not completed in time.',
  payment_provider_unavailable: 'Payments were temporarily unavailable.',
  cancelled_by_customer: 'You cancelled this order.',
}
