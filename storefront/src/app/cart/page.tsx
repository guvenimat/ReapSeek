import type { Metadata } from 'next'
import { CartView } from '@/components/cart/CartView'
import { getCart } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = { title: 'Cart', robots: { index: false } }

export default async function CartPage() {
  const [cart, user] = await Promise.all([getCart(), getCurrentUser()])
  return (
    <div className="container cart-page">
      <h1>Your cart</h1>
      <CartView
        initial={cart ?? { packages: [], item_count: 0, subtotal: 0, shipping_total: 0, total: 0, currency: 'TRY', can_checkout: false, warnings: [] }}
        signedIn={user !== null}
      />
    </div>
  )
}
