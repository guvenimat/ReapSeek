import Link from 'next/link'
import { ProductCard } from '@/components/ProductCard'
import { RecommendationShelf } from '@/components/recommendations/RecommendationShelf'
import { ReturnIcon, ShieldIcon, StoreIcon, TruckIcon } from '@/components/icons'
import { departmentHue, getCategories, search, type ProductCard as Card } from '@/lib/catalog'

const promises = [
  { icon: TruckIcon, title: 'Fast delivery', text: 'Most orders ship within 24 hours' },
  { icon: ReturnIcon, title: 'Easy returns', text: '15 days to change your mind' },
  { icon: ShieldIcon, title: 'Secure payment', text: '3D Secure on every card payment' },
  { icon: StoreIcon, title: 'Trusted sellers', text: 'Every seller is verified by ReapSeek' },
]

export default async function HomePage() {
  const [departments, deals, fresh] = await Promise.all([
    getCategories(),
    search({ sort: 'discount', in_stock: 'true', page_size: '10' }),
    search({ sort: 'newest', in_stock: 'true', page_size: '10' }),
  ])

  return (
    <div className="container home">
      <section className="hero" data-testid="hero">
        <div className="hero-copy">
          <span className="eyebrow">Autumn season</span>
          <h1>Everything you need, from hundreds of sellers</h1>
          <p>Compare offers on the same product, pick the seller you trust and get it delivered fast.</p>
          <Link href="/search?sort=discount" className="btn btn-accent">Shop the deals</Link>
        </div>
        <div className="hero-art" aria-hidden="true">
          <span className="blob b1" />
          <span className="blob b2" />
          <span className="blob b3" />
        </div>
      </section>

      <section className="promises" aria-label="Why ReapSeek">
        {promises.map(({ icon: IconComponent, title, text }) => (
          <div key={title} className="promise">
            <IconComponent size={26} />
            <div>
              <strong>{title}</strong>
              <span>{text}</span>
            </div>
          </div>
        ))}
      </section>

      <RecommendationShelf placement="home_popular" title="Popular right now" />

      <Shelf title="Today's deals" href="/search?sort=discount&in_stock=true" products={deals.result?.products ?? []} testId="shelf-deals" />

      <section className="section">
        <div className="section-head">
          <h2>Shop by category</h2>
        </div>
        <div className="category-grid" data-testid="category-grid">
          {departments.map((d) => (
            <Link key={d.slug} href={`/c/${d.slug}`} className="category-tile" style={{ ['--hue' as string]: departmentHue(d.slug) }}>
              <span className="tile-art" aria-hidden="true">
                <img src={`/media/catalog/${d.image_kind}/white/${d.id}.svg`} alt="" width={80} height={100} />
              </span>
              <strong>{d.name}</strong>
              <span>{d.children.slice(0, 3).map((c) => c.name).join(', ')}</span>
            </Link>
          ))}
        </div>
      </section>

      <RecommendationShelf placement="home_trending" title="Trending this week" />

      <Shelf title="New arrivals" href="/search?sort=newest" products={fresh.result?.products ?? []} testId="shelf-new" />
    </div>
  )
}

function Shelf({ title, href, products, testId }: { title: string; href: string; products: Card[]; testId: string }) {
  if (products.length === 0) return null
  return (
    <section className="section" data-testid={testId}>
      <div className="section-head">
        <h2>{title}</h2>
        <Link href={href} className="see-all">See all</Link>
      </div>
      <div className="shelf">
        {products.map((p) => <ProductCard key={p.id} product={p} />)}
      </div>
    </section>
  )
}
