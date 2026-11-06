import Link from 'next/link'
import type { CategoryNode } from '@/lib/catalog'
import { LogoMark } from './icons'

export function Footer({ departments }: { departments: CategoryNode[] }) {
  return (
    <footer className="site-footer">
      <div className="container footer-grid">
        <div className="footer-brand">
          <Link href="/" className="logo" aria-label="ReapSeek home">
            <LogoMark size={30} />
            <span className="logo-text">ReapSeek</span>
          </Link>
          <p>A marketplace of independent sellers. Every price is live, every order is split into seller packages and shipped by them.</p>
        </div>
        <div className="footer-col">
          <h2>Shop</h2>
          {departments.slice(0, 5).map((d) => <Link key={d.slug} href={`/c/${d.slug}`}>{d.name}</Link>)}
        </div>
        <div className="footer-col">
          <h2>Help</h2>
          <Link href="/account">Track an order</Link>
          <Link href="/account">Returns</Link>
          <Link href="/forgot-password">Reset password</Link>
        </div>
        <div className="footer-col">
          <h2>Your account</h2>
          <Link href="/account">Orders</Link>
          <Link href="/wishlist">Favorites</Link>
          <Link href="/cart">Cart</Link>
        </div>
      </div>
      <div className="container footer-legal">
        <span>© 2026 ReapSeek · Prices in TRY, VAT included</span>
        <span>
          A portfolio project, no real orders or payments. Analytics and recommendations by ReapDeep.{' '}
          <a href="https://github.com/guvenimat/ReapSeek" rel="noreferrer">Source on GitHub</a>
        </span>
      </div>
    </footer>
  )
}
