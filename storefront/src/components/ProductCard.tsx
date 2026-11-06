'use client'

import Link from 'next/link'
import { track } from '@/lib/analytics'
import { formatPrice, type ProductCard as Card } from '@/lib/catalog'
import { Stars } from './reviews/Stars'
import { WishlistButton } from './wishlist/Wishlist'

export type CardTracking = { query: string; position: number }

const freeShippingFrom = 500

export function ProductCard({ product, tracking, onSelect }: { product: Card; tracking?: CardTracking; onSelect?: () => void }) {
  const click = () => {
    if (tracking) track('search_result_clicked', { query: tracking.query, item_id: String(product.id), position: tracking.position, price: product.price })
    onSelect?.()
  }
  const title = product.title.startsWith(product.brand) ? product.title.slice(product.brand.length).trim() : product.title
  return (
    <Link href={`/p/${product.slug}`} className={`product-card${product.in_stock ? '' : ' sold-out'}`} data-product={product.id} onClick={click}>
      <div className="product-media">
        {product.image_url && <img src={product.image_url} alt={product.title} width={400} height={500} loading="lazy" />}
        {product.discount > 0 && product.in_stock && <span className="discount-badge">-{product.discount}%</span>}
        {!product.in_stock && <span className="stock-badge">Out of stock</span>}
        <WishlistButton productId={product.id} price={product.price} />
      </div>
      <div className="product-info">
        <p className="product-brand">{product.brand}</p>
        <p className="product-title">{title}</p>
        {product.rating != null && (product.review_count ?? 0) > 0 ? (
          <p className="card-rating"><Stars value={product.rating} /> <b>{product.rating.toFixed(1)}</b> <span className="muted">({product.review_count})</span></p>
        ) : (
          <p className="card-rating empty">New</p>
        )}
        {product.price !== null ? (
          <div className="product-price">
            <span className="price">{formatPrice(product.price)}</span>
            {product.list_price !== null && <span className="list-price">{formatPrice(product.list_price)}</span>}
          </div>
        ) : (
          <div className="product-price muted">Currently unavailable</div>
        )}
        <p className="card-meta">
          {product.price !== null && product.price >= freeShippingFrom && <span className="ship-chip">Free shipping</span>}
          {product.seller_count > 1 && <span className="seller-count">{product.seller_count} sellers</span>}
        </p>
      </div>
    </Link>
  )
}
