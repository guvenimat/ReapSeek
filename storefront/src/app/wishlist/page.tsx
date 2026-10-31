import type { Metadata } from 'next'
import Link from 'next/link'
import { redirect } from 'next/navigation'
import { ProductCard } from '@/components/ProductCard'
import { formatPrice } from '@/lib/catalog'
import { getWishlist } from '@/lib/orders'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = { title: 'Favorites', robots: { index: false } }

export default async function WishlistPage() {
  if (!(await getCurrentUser())) redirect('/login?next=/wishlist')
  const items = (await getWishlist()) ?? []
  return (
    <div className="container section wishlist-page" data-testid="wishlist-page">
      <div className="section-head">
        <h1>Favorites</h1>
        <span className="muted">{items.length} {items.length === 1 ? 'product' : 'products'}</span>
      </div>
      <p className="muted">We email you when a product on this list gets at least 5% cheaper.</p>
      {items.length === 0 ? (
        <div className="empty-state">
          <p>Nothing saved yet. Tap the heart on any product to keep it here.</p>
          <Link href="/" className="btn btn-accent">Start shopping</Link>
        </div>
      ) : (
        <div className="product-grid">
          {items.map((i) => (
            <div key={i.product.id} className="wishlist-item" data-testid="wishlist-item" data-product={i.product.id}>
              <ProductCard product={i.product} />
              {i.price_drop !== null && i.added_price !== null && (
                <p className="price-drop" data-testid="price-drop">{formatPrice(i.price_drop)} cheaper than when you saved it</p>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
