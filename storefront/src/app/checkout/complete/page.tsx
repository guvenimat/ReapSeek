import type { Metadata } from 'next'
import { notFound, redirect } from 'next/navigation'
import { PaymentResult } from '@/components/checkout/PaymentResult'
import { getOrder } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = { title: 'Order status', robots: { index: false } }

export default async function CheckoutCompletePage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const number = (await searchParams).order
  if (typeof number !== 'string' || number.length > 32) notFound()
  if (!(await getCurrentUser())) redirect(`/login?next=${encodeURIComponent(`/checkout/complete?order=${number}`)}`)
  const order = await getOrder(number)
  if (!order) notFound()
  return (
    <div className="container result-page">
      <PaymentResult initial={order} />
    </div>
  )
}
