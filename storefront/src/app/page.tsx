import { cookies } from 'next/headers'
import Link from 'next/link'
import { ProductCard } from '@/components/ProductCard'
import { RecentlyViewed } from '@/components/wishlist/RecentlyViewed'
import { RecommendationShelf } from '@/components/recommendations/RecommendationShelf'
import { ChevronIcon, ReturnIcon, ShieldIcon, StoreIcon, TruckIcon } from '@/components/icons'
import { getCategories, search, type CategoryNode, type ProductCard as Card } from '@/lib/catalog'

const perks = [
  { icon: TruckIcon, title: 'Free shipping', text: 'from ₺500 per seller' },
  { icon: ReturnIcon, title: 'Easy returns', text: 'within 7 days of delivery' },
  { icon: ShieldIcon, title: 'Secure payment', text: '3-D Secure on every card' },
  { icon: StoreIcon, title: 'Trusted sellers', text: 'competing on every product' },
]

const tints = ['var(--tint-coral)', 'var(--tint-blue)', 'var(--tint-sand)', 'var(--tint-yellow)', 'var(--tint-green)', 'var(--tint-violet)']

export default async function HomePage() {
  const signedIn = (await cookies()).has('rs_session')
  const [departments, deals, fresh] = await Promise.all([
    getCategories(),
    search({ sort: 'discount', in_stock: 'true', page_size: '10' }),
    search({ sort: 'newest', in_stock: 'true', page_size: '10' }),
  ])
  const dealProducts = deals.result?.products ?? []
  const heroProducts = dealProducts.filter((p) => p.image_url).slice(0, 3)
  const promos = pickPromos(departments)

  return (
    <div className="container home">
      <section className="hero-row">
        <div className="hero" data-testid="hero">
          <div className="hero-copy">
            <span className="eyebrow">Today&apos;s edit · up to {Math.max(0, ...dealProducts.map((p) => p.discount))}% off</span>
            <h1>Everything you love, at the best price.</h1>
            <p>Sellers compete on every product, so the best offer is always on top.</p>
            <div className="hero-actions">
              <Link href="/search?sort=discount&in_stock=true" className="btn btn-light">Shop the deals</Link>
              <Link href="#categories" className="btn btn-outline-light">Browse categories</Link>
            </div>
          </div>
          {heroProducts.length === 3 && (
            <div className="hero-art">
              {heroProducts.map((p, i) => (
                <Link key={p.id} href={`/p/${p.slug}`} className={`hero-tile t${i + 1}`} aria-label={p.title}>
                  <img src={p.image_url!} alt="" width={400} height={500} />
                  {p.discount > 0 && <span className="hero-tile-badge">-{p.discount}%</span>}
                </Link>
              ))}
            </div>
          )}
        </div>
        <div className="hero-side">
          {promos.map((d, i) => (
            <Link key={d.slug} href={`/c/${d.slug}?sort=discount`} className={`promo p${i + 1}`}>
              <span className="promo-eyebrow">{d.name}</span>
              <strong>{i === 0 ? `Best deals in ${d.name.toLowerCase()}` : `New in: ${d.children.slice(0, 2).map((c) => c.name.toLowerCase()).join(' & ')}`}</strong>
              <span className="promo-link">Shop now <ChevronIcon size={16} /></span>
              <img src={`/media/catalog/${d.image_kind}/${i === 0 ? 'blue' : 'yellow'}/${d.id}.svg`} alt="" width={160} height={200} />
            </Link>
          ))}
        </div>
      </section>

      <section className="perks" aria-label="Why ReapSeek">
        {perks.map(({ icon: IconComponent, title, text }) => (
          <div key={title} className="perk">
            <IconComponent size={26} />
            <span><strong>{title}</strong> {text}</span>
          </div>
        ))}
      </section>

      <section className="section" id="categories">
        <div className="section-head">
          <h2>Shop by category</h2>
        </div>
        <div className="category-grid" data-testid="category-grid">
          {departments.map((d, i) => (
            <Link key={d.slug} href={`/c/${d.slug}`} className="category-tile">
              <span className="tile-art" aria-hidden="true" style={{ background: tints[i % tints.length] }}>
                <img src={`/media/catalog/${d.image_kind}/${['orange', 'blue', 'brown', 'yellow', 'green', 'purple'][i % 6]}/${d.id}.svg`} alt="" width={80} height={100} />
              </span>
              <strong>{d.name}</strong>
            </Link>
          ))}
        </div>
      </section>

      {signedIn && <RecommendationShelf placement="home_for_you" title="Recommended for you" skeleton={false} />}

      <RecommendationShelf placement="home_popular" title="Popular right now" />

      <Shelf title="Deals of the day" eyebrow="Biggest discounts" href="/search?sort=discount&in_stock=true" products={dealProducts} testId="shelf-deals" />

      <RecentlyViewed />

      <RecommendationShelf placement="home_trending" title="Trending this week" />

      <Shelf title="New arrivals" eyebrow="Just listed" href="/search?sort=newest" products={fresh.result?.products ?? []} testId="shelf-new" />
    </div>
  )
}

function pickPromos(departments: CategoryNode[]): CategoryNode[] {
  const find = (re: RegExp) => departments.find((d) => re.test(d.slug))
  const picked = [find(/electronic/), find(/home/)].filter((d): d is CategoryNode => d !== undefined)
  for (const d of departments) if (picked.length < 2 && !picked.includes(d)) picked.push(d)
  return picked.slice(0, 2)
}

function Shelf({ title, eyebrow, href, products, testId }: { title: string; eyebrow: string; href: string; products: Card[]; testId: string }) {
  if (products.length === 0) return null
  return (
    <section className="section" data-testid={testId}>
      <div className="section-head">
        <div>
          <span className="section-eyebrow">{eyebrow}</span>
          <h2>{title}</h2>
        </div>
        <Link href={href} className="see-all">See all <ChevronIcon size={16} /></Link>
      </div>
      <div className="shelf">
        {products.map((p) => <ProductCard key={p.id} product={p} />)}
      </div>
    </section>
  )
}
