import Link from 'next/link'
import type { User } from '@/lib/session'
import { categories } from '@/lib/categories'
import { CartIcon, HeartIcon, LogoMark, SearchIcon, StoreIcon, UserIcon } from './icons'

export function Header({ user }: { user: User | null }) {
  return (
    <header className="site-header">
      <div className="utility-bar">
        <div className="container utility-inner">
          <span>Free shipping on orders over ₺500</span>
          <nav aria-label="Utility">
            <span className="utility-link"><StoreIcon size={14} /> Sell on ReapSeek</span>
            <span className="utility-link">Help &amp; support</span>
          </nav>
        </div>
      </div>
      <div className="container main-bar">
        <Link href="/" className="logo" aria-label="ReapSeek home">
          <LogoMark />
          <span className="logo-text">Reap<strong>Seek</strong></span>
        </Link>
        <form className="search" role="search" action="/">
          <SearchIcon size={18} className="search-icon" />
          <input type="search" name="q" placeholder="Search for products, brands and categories" aria-label="Search" disabled
            title="Search is coming soon" />
          <button type="submit" disabled>Search</button>
        </form>
        <nav className="header-actions" aria-label="Account">
          {user ? (
            <Link href="/account" className="header-action" data-testid="account-link">
              <UserIcon />
              <span><small>Hello,</small>{user.name.split(' ')[0]}</span>
            </Link>
          ) : (
            <Link href="/login" className="header-action" data-testid="sign-in-link">
              <UserIcon />
              <span><small>Welcome</small>Sign in</span>
            </Link>
          )}
          <span className="header-action muted"><HeartIcon /><span><small>Saved</small>Favorites</span></span>
          <span className="header-action muted cart"><CartIcon /><span className="badge">0</span><span><small>Your</small>Cart</span></span>
        </nav>
      </div>
      <nav className="category-bar" aria-label="Categories">
        <div className="container category-inner">
          {categories.map((c) => (
            <Link key={c.slug} href={`/c/${c.slug}`}>{c.name}</Link>
          ))}
        </div>
      </nav>
    </header>
  )
}
