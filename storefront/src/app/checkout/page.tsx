import type { Metadata } from 'next'
import { redirect } from 'next/navigation'
import { CheckoutForm } from '@/components/checkout/CheckoutForm'
import { getAddresses, getCart } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = { title: 'Checkout', robots: { index: false } }

export default async function CheckoutPage() {
  const user = await getCurrentUser()
  if (!user) redirect('/login?next=/checkout')
  const [cart, addresses] = await Promise.all([getCart(), getAddresses()])
  if (!cart || cart.packages.length === 0) redirect('/cart')
  return (
    <div className="container checkout-page">
      <h1>Checkout</h1>
      <CheckoutForm cart={cart} addresses={addresses} customerName={user.name} />
    </div>
  )
}
