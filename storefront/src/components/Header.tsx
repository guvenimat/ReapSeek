import Link from 'next/link'
import { Suspense } from 'react'
import type { CategoryNode } from '@/lib/catalog'
import type { User } from '@/lib/session'
import { CategoryNav } from './CategoryNav'
import { CartIcon, HeartIcon, LogoMark, UserIcon } from './icons'
import { MobileMenu } from './MobileMenu'
import { SearchBox } from './SearchBox'

export function Header({ user, departments, cartCount }: { user: User | null; departments: CategoryNode[]; cartCount: number }) {
  const firstName = user ? user.name.split(' ')[0] : null
  return (
    <header className="site-header">
      <div className="utility-bar">
        <div className="container utility-inner">
          <span>Free shipping from ₺500 per seller · Easy returns</span>
          <nav aria-label="Utility">
            <Link href="/account" className="utility-link">Track order</Link>
            <a href="https://github.com/guvenimat/ReapSeek" className="utility-link" rel="noreferrer">Sell on ReapSeek</a>
          </nav>
        </div>
      </div>
      <div className="container main-bar">
        <Suspense fallback={<span className="menu-button" />}>
          <MobileMenu departments={departments} userName={firstName} />
        </Suspense>
        <Link href="/" className="logo" aria-label="ReapSeek home">
          <LogoMark />
          <span className="logo-text">ReapSeek</span>
        </Link>
        <Suspense fallback={<div className="search" />}>
          <SearchBox />
        </Suspense>
        <nav className="header-actions" aria-label="Account">
          {user ? (
            <Link href="/account" className="header-action" data-testid="account-link" aria-label={`Account of ${firstName}`}>
              <UserIcon size={24} />
              <span className="action-label">{firstName}</span>
            </Link>
          ) : (
            <Link href="/login" className="header-action" data-testid="sign-in-link" aria-label="Sign in">
              <UserIcon size={24} />
              <span className="action-label">Sign in</span>
            </Link>
          )}
          <Link href="/wishlist" className="header-action" data-testid="wishlist-link" aria-label="Favorites">
            <HeartIcon size={24} />
            <span className="action-label">Favorites</span>
          </Link>
          <Link href="/cart" className="header-action cart" data-testid="cart-link" aria-label="Cart">
            <CartIcon size={24} />
            {cartCount > 0 && <span className="badge" data-testid="cart-count">{cartCount}</span>}
            <span className="action-label">Cart</span>
          </Link>
        </nav>
      </div>
      <Suspense fallback={<nav className="category-bar" />}>
        <CategoryNav departments={departments} />
      </Suspense>
    </header>
  )
}
