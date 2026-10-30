import type { Metadata } from 'next'
import Link from 'next/link'
import { notFound } from 'next/navigation'
import { cache } from 'react'
import { TrackEvent } from '@/components/analytics/Analytics'
import { ProductView } from '@/components/product/ProductView'
import { buyBox, getProduct, initialVariant, publicUrl, type StoreProduct } from '@/lib/products'

type Props = { params: Promise<{ slug: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }

const load = cache(getProduct)

function summary(product: StoreProduct) {
  const text = product.description || `${product.title} from ${product.brand}.`
  return text.length > 155 ? `${text.slice(0, 152)}...` : text
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const product = await load((await params).slug)
  if (!product) return {}
  const image = product.images[0] ? `${publicUrl}${product.images[0].url}` : undefined
  return {
    title: product.title,
    description: summary(product),
    alternates: { canonical: `/p/${product.slug}` },
    openGraph: { title: product.title, description: summary(product), url: `/p/${product.slug}`, type: 'website', images: image ? [image] : [] },
  }
}

export default async function ProductPage({ params, searchParams }: Props) {
  const product = await load((await params).slug)
  if (!product) notFound()
  const query = await searchParams
  const requested = Number(Array.isArray(query.variant) ? query.variant[0] : query.variant)
  const variant = initialVariant(product, Number.isFinite(requested) ? requested : undefined)
  const offer = buyBox(variant)
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Istanbul' }).format(new Date())
  const leaf = product.categories[product.categories.length - 1]

  const jsonLd = [
    {
      '@context': 'https://schema.org',
      '@type': 'Product',
      name: product.title,
      description: product.description,
      sku: variant.sku,
      brand: { '@type': 'Brand', name: product.brand },
      category: product.categories.map((c) => c.name).join(' > '),
      image: product.images.map((i) => `${publicUrl}${i.url}`),
      url: `${publicUrl}/p/${product.slug}`,
      offers: {
        '@type': 'AggregateOffer',
        priceCurrency: 'TRY',
        lowPrice: product.min_price ?? undefined,
        highPrice: product.max_price ?? undefined,
        offerCount: product.variants.reduce((n, v) => n + v.offers.length, 0),
        availability: product.min_price !== null ? 'https://schema.org/InStock' : 'https://schema.org/OutOfStock',
      },
    },
    {
      '@context': 'https://schema.org',
      '@type': 'BreadcrumbList',
      itemListElement: [
        { '@type': 'ListItem', position: 1, name: 'Home', item: `${publicUrl}/` },
        ...product.categories.map((c, i) => ({ '@type': 'ListItem', position: i + 2, name: c.name, item: `${publicUrl}/c/${c.slug}` })),
        { '@type': 'ListItem', position: product.categories.length + 2, name: product.title },
      ],
    },
  ]

  return (
    <div className="container product-page">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, '\\u003c') }} />
      <nav className="breadcrumb" aria-label="Breadcrumb">
        <Link href="/">Home</Link>
        {product.categories.map((c) => (
          <span key={c.path} className="crumb"><span>/</span><Link href={`/c/${c.slug}`}>{c.name}</Link></span>
        ))}
      </nav>

      <ProductView product={product} initialVariantId={variant.id} today={today} />

      <section className="pdp-details" data-testid="product-details">
        <div>
          <h2>Product details</h2>
          <p>{product.description}</p>
        </div>
        {Object.keys(product.attributes).length > 0 && (
          <dl className="attribute-list">
            <div><dt>Brand</dt><dd>{product.brand}</dd></div>
            {Object.entries(product.attributes).map(([k, v]) => (
              <div key={k}><dt>{k}</dt><dd>{v}</dd></div>
            ))}
          </dl>
        )}
      </section>

      <TrackEvent
        event="product_viewed"
        onceKey={`${product.id}`}
        properties={{
          item_id: String(product.id),
          title: product.title,
          brand: product.brand,
          category: leaf?.slug ?? '',
          department: product.categories[0]?.slug ?? '',
          price: offer?.price ?? null,
          seller_id: offer?.seller_id ?? null,
          seller_count: product.seller_count,
          in_stock: product.min_price !== null,
        }}
      />
    </div>
  )
}
