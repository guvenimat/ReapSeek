import Link from 'next/link'
import { ReturnIcon, ShieldIcon, StoreIcon, TruckIcon } from '@/components/icons'
import { categories } from '@/lib/categories'

const promises = [
  { icon: TruckIcon, title: 'Fast delivery', text: 'Most orders ship within 24 hours' },
  { icon: ReturnIcon, title: 'Easy returns', text: '15 days to change your mind' },
  { icon: ShieldIcon, title: 'Secure payment', text: '3D Secure on every card payment' },
  { icon: StoreIcon, title: 'Trusted sellers', text: 'Every seller is verified by ReapSeek' },
]

export default function HomePage() {
  return (
    <div className="container home">
      <section className="hero" data-testid="hero">
        <div className="hero-copy">
          <span className="eyebrow">Autumn season</span>
          <h1>Everything you need, from hundreds of sellers</h1>
          <p>Compare offers on the same product, pick the seller you trust and get it delivered fast.</p>
          <Link href="/c/women" className="btn btn-accent">Shop the new season</Link>
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

      <section className="section">
        <div className="section-head">
          <h2>Shop by category</h2>
        </div>
        <div className="category-grid" data-testid="category-grid">
          {categories.map((c) => (
            <Link key={c.slug} href={`/c/${c.slug}`} className="category-tile" style={{ ['--hue' as string]: c.hue }}>
              <span className="tile-art" aria-hidden="true">{c.name.charAt(0)}</span>
              <strong>{c.name}</strong>
              <span>{c.tagline}</span>
            </Link>
          ))}
        </div>
      </section>
    </div>
  )
}
