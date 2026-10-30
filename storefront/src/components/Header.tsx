import Link from 'next/link'
import { Suspense } from 'react'
import type { CategoryNode } from '@/lib/catalog'
import type { User } from '@/lib/session'
import { CartIcon, HeartIcon, LogoMark, StoreIcon, UserIcon } from './icons'
import { SearchBox } from './SearchBox'

export function Header({ user, departments }: { user: User | null; departments: CategoryNode[] }) {
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
        <Suspense fallback={<div className="search" />}>
          <SearchBox />
        </Suspense>
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
          {departments.map((d) => (
            <div key={d.slug} className="category-menu">
              <Link href={`/c/${d.slug}`}>{d.name}</Link>
              {d.children.length > 0 && (
                <div className="category-dropdown">
                  {d.children.map((c) => <Link key={c.slug} href={`/c/${c.slug}`}>{c.name}</Link>)}
                </div>
              )}
            </div>
          ))}
        </div>
      </nav>
    </header>
  )
}
