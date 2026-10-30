import Link from 'next/link'
import { formatPrice, type ProductCard as Card } from '@/lib/catalog'

export function ProductCard({ product }: { product: Card }) {
  return (
    <Link href={`/p/${product.slug}`} className={`product-card${product.in_stock ? '' : ' sold-out'}`} data-product={product.id}>
      <div className="product-media">
        {product.image_url && <img src={product.image_url} alt={product.title} width={400} height={500} loading="lazy" />}
        {product.discount > 0 && product.in_stock && <span className="discount-badge">-{product.discount}%</span>}
        {!product.in_stock && <span className="stock-badge">Out of stock</span>}
      </div>
      <div className="product-info">
        <p className="product-title"><strong>{product.brand}</strong> {product.title.startsWith(product.brand) ? product.title.slice(product.brand.length).trim() : product.title}</p>
        {product.price !== null ? (
          <div className="product-price">
            {product.list_price !== null && <span className="list-price">{formatPrice(product.list_price)}</span>}
            <span className="price">{formatPrice(product.price)}</span>
          </div>
        ) : (
          <div className="product-price muted">Currently unavailable</div>
        )}
        {product.seller_count > 1 && <p className="seller-count">{product.seller_count} sellers</p>}
      </div>
    </Link>
  )
}
