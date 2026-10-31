'use client'

import Link from 'next/link'
import { Stars } from '@/components/reviews/Stars'
import { useMemo, useState } from 'react'
import { formatPrice } from '@/lib/catalog'
import { buyBox, type StoreOffer, type StoreProduct, type StoreVariant } from '@/lib/products'
import { ReturnIcon, ShieldIcon, TruckIcon } from '../icons'
import { AddToCart } from './AddToCart'

const swatches: Record<string, string> = {
  black: '#2b2d33', white: '#f4f3ef', grey: '#9aa0a6', navy: '#1f3a68', blue: '#2f6fdb', red: '#d6453d', green: '#2f8f5b', beige: '#d9c3a0',
  pink: '#e78fb3', brown: '#8b5a3c', silver: '#c0c4cc', gold: '#d4af37', purple: '#7e57c2', orange: '#f08a24', yellow: '#f2c230',
}

export function deliveryDate(handlingDays: number, today: string) {
  const date = new Date(`${today}T00:00:00Z`)
  let days = handlingDays + 2
  while (days > 0) {
    date.setUTCDate(date.getUTCDate() + 1)
    if (date.getUTCDay() !== 0) days--
  }
  return date.toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'UTC' })
}

export function ProductView({ product, initialVariantId, today, rating }: {
  product: StoreProduct; initialVariantId: number; today: string; rating: { average: number; count: number } | null
}) {
  const [variantId, setVariantId] = useState(initialVariantId)
  const [chosenOfferId, setChosenOfferId] = useState<number | null>(null)
  const variant = product.variants.find((v) => v.id === variantId) ?? product.variants[0]
  const offer: StoreOffer | undefined = variant.offers.find((o) => o.id === chosenOfferId) ?? buyBox(variant)
  const others = variant.offers.filter((o) => o.id !== offer?.id)
  const color = variant.options.Color
  const images = useMemo(() => {
    const own = product.images.filter((i) => i.color === color)
    return own.length > 0 ? [...own, ...product.images.filter((i) => i.color !== color)] : product.images
  }, [product.images, color])
  const [imageIndex, setImageIndex] = useState(0)
  const image = images[Math.min(imageIndex, images.length - 1)]

  const select = (axis: string, value: string) => {
    const wanted = { ...variant.options, [axis]: value }
    const matches = (v: StoreVariant, keys: string[]) => keys.every((k) => v.options[k] === wanted[k])
    const keys = Object.keys(wanted)
    const next =
      product.variants.find((v) => matches(v, keys) && v.offers.length > 0) ??
      product.variants.find((v) => matches(v, keys)) ??
      product.variants.find((v) => v.options[axis] === value && v.offers.length > 0) ??
      product.variants.find((v) => v.options[axis] === value)
    if (!next) return
    setVariantId(next.id)
    setChosenOfferId(null)
    if (axis === 'Color') setImageIndex(0)
    const url = new URL(window.location.href)
    url.searchParams.set('variant', String(next.id))
    window.history.replaceState(null, '', url)
  }

  const available = (axis: string, value: string) => {
    const wanted = { ...variant.options, [axis]: value }
    const others = Object.keys(wanted).filter((k) => k !== axis && k !== 'Size')
    return product.variants.some((v) => v.options[axis] === value && others.every((k) => v.options[k] === wanted[k]) && v.offers.length > 0)
  }

  return (
    <div className="pdp" data-testid="product-view" data-variant={variant.id}>
      <div className="pdp-gallery">
        <div className="pdp-image">
          {image && <img src={image.url} alt={image.alt} width={400} height={500} />}
          {offer && offer.discount > 0 && <span className="discount-badge">-{offer.discount}%</span>}
        </div>
        {images.length > 1 && (
          <div className="pdp-thumbs">
            {images.map((i, n) => (
              <button key={i.url} type="button" className={n === imageIndex ? 'active' : ''} onClick={() => setImageIndex(n)} aria-label={i.alt}>
                <img src={i.url} alt="" width={64} height={80} />
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="pdp-main">
        <Link href={`/search?brand=${encodeURIComponent(product.brand)}`} className="pdp-brand">{product.brand}</Link>
        <h1 data-testid="product-title">{product.title}</h1>
        {rating && (
          <a href="#reviews" className="pdp-rating" data-testid="product-rating">
            <Stars value={rating.average} /> <strong>{rating.average.toFixed(1)}</strong> <span className="muted">({rating.count} review{rating.count === 1 ? '' : 's'})</span>
          </a>
        )}
        {product.seller_count > 1 && <p className="muted">Offered by {product.seller_count} sellers</p>}

        <div className="pdp-price" data-testid="product-price">
          {offer ? (
            <>
              {offer.list_price !== null && <span className="list-price">{formatPrice(offer.list_price)}</span>}
              <span className="price">{formatPrice(offer.price)}</span>
              {offer.discount > 0 && <span className="discount-chip">{offer.discount}% off</span>}
            </>
          ) : (
            <span className="unavailable">This option is out of stock</span>
          )}
        </div>

        {product.axes.map((axis) => (
          <div key={axis.name} className="pdp-axis" data-axis={axis.name}>
            <div className="pdp-axis-head">
              <strong>{axis.name}</strong>
              <span className="muted">{variant.options[axis.name]}</span>
            </div>
            <div className="pdp-options">
              {axis.values.map((value) => {
                const selected = variant.options[axis.name] === value
                const enabled = available(axis.name, value)
                return axis.name === 'Color' ? (
                  <button key={value} type="button" className={`color-option${selected ? ' selected' : ''}${enabled ? '' : ' unavailable'}`}
                    onClick={() => select(axis.name, value)} aria-pressed={selected} title={value}>
                    <span className="swatch-dot" style={{ background: swatches[value.toLowerCase()] ?? '#cfd3df' }} />
                  </button>
                ) : (
                  <button key={value} type="button" className={`size-option${selected ? ' selected' : ''}${enabled ? '' : ' unavailable'}`}
                    onClick={() => select(axis.name, value)} aria-pressed={selected}>
                    {value}
                  </button>
                )
              })}
            </div>
          </div>
        ))}

        {offer && (
          <div className="seller-card" data-testid="buy-box" data-seller={offer.seller_id}>
            <div className="seller-line">
              <span>Sold by <Link href={`/s/${offer.seller_slug}`} data-testid="seller-link"><strong>{offer.seller_name}</strong></Link></span>
              <span className="rating">★ {offer.seller_rating.toFixed(1)}</span>
            </div>
            <div className="delivery"><TruckIcon size={18} /><span>Ships in {offer.handling_days} {offer.handling_days === 1 ? 'day' : 'days'} · Estimated delivery <strong>{deliveryDate(offer.handling_days, today)}</strong></span></div>
            {offer.low_stock !== null && <div className="low-stock">Only {offer.low_stock} left from this seller</div>}
          </div>
        )}

        <AddToCart key={`${variant.id}-${offer?.id ?? 0}`} productId={product.id} offer={offer} />

        <ul className="pdp-promises">
          <li><TruckIcon size={18} /> Free shipping over ₺500</li>
          <li><ReturnIcon size={18} /> 15-day returns</li>
          <li><ShieldIcon size={18} /> Buyer protection</li>
        </ul>

        {others.length > 0 && (
          <section className="other-sellers" data-testid="other-sellers">
            <h2>Other sellers ({others.length})</h2>
            {others.map((o) => (
              <div key={o.id} className="other-seller" data-seller={o.seller_id}>
                <div>
                  <Link href={`/s/${o.seller_slug}`}><strong>{o.seller_name}</strong></Link> <span className="rating">★ {o.seller_rating.toFixed(1)}</span>
                  <div className="muted small">Delivery {deliveryDate(o.handling_days, today)}{o.low_stock !== null ? ` · Only ${o.low_stock} left` : ''}</div>
                </div>
                <span className="price">{formatPrice(o.price)}</span>
                <button type="button" className="btn btn-ghost btn-sm" onClick={() => setChosenOfferId(o.id)}>Choose</button>
              </div>
            ))}
          </section>
        )}
      </div>
    </div>
  )
}
