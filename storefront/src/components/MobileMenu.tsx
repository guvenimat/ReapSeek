'use client'

import Link from 'next/link'
import { usePathname, useSearchParams } from 'next/navigation'
import { useEffect, useRef, useState } from 'react'
import type { CategoryNode } from '@/lib/catalog'
import { CartIcon, ChevronIcon, CloseIcon, HeartIcon, LogoMark, MenuIcon, UserIcon } from './icons'

export function MobileMenu({ departments, userName }: { departments: CategoryNode[]; userName: string | null }) {
  const [open, setOpen] = useState(false)
  const [expanded, setExpanded] = useState<string | null>(null)
  const closeButton = useRef<HTMLButtonElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const pathname = usePathname()
  const search = useSearchParams()

  useEffect(() => setOpen(false), [pathname, search])

  useEffect(() => {
    if (!open) return
    document.body.classList.add('no-scroll')
    closeButton.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    const onResize = () => {
      if (window.matchMedia('(min-width: 960px)').matches) setOpen(false)
    }
    document.addEventListener('keydown', onKey)
    window.addEventListener('resize', onResize)
    const button = trigger.current
    return () => {
      document.body.classList.remove('no-scroll')
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('resize', onResize)
      button?.focus()
    }
  }, [open])

  return (
    <>
      <button ref={trigger} type="button" className="menu-button" aria-label="Open menu" aria-expanded={open} aria-controls="mobile-menu" onClick={() => setOpen(true)}>
        <MenuIcon size={24} />
      </button>
      <div className={`drawer-backdrop${open ? ' open' : ''}`} onClick={() => setOpen(false)} aria-hidden="true" />
      <aside id="mobile-menu" className={`drawer${open ? ' open' : ''}`} role="dialog" aria-modal="true" aria-label="Menu" aria-hidden={!open} inert={!open}
        onClick={(e) => {
          if ((e.target as HTMLElement).closest('a')) setOpen(false)
        }}>
        <div className="drawer-head">
          <Link href="/" className="logo">
            <LogoMark size={30} />
            <span className="logo-text">ReapSeek</span>
          </Link>
          <button ref={closeButton} type="button" className="icon-button" aria-label="Close menu" onClick={() => setOpen(false)}>
            <CloseIcon size={22} />
          </button>
        </div>
        <Link href={userName ? '/account' : '/login'} className="drawer-account" onClick={() => setOpen(false)}>
          <span className="drawer-avatar"><UserIcon size={20} /></span>
          <span>
            <small>{userName ? 'Hello,' : 'Welcome'}</small>
            <strong>{userName ?? 'Sign in or register'}</strong>
          </span>
          <ChevronIcon size={18} />
        </Link>
        <nav className="drawer-nav" aria-label="Categories">
          {departments.map((d) => {
            const isOpen = expanded === d.slug
            return (
              <div key={d.slug} className={`drawer-group${isOpen ? ' open' : ''}`}>
                <button type="button" className="drawer-row" aria-expanded={isOpen} onClick={() => setExpanded(isOpen ? null : d.slug)}>
                  <span className="drawer-thumb" aria-hidden="true">
                    <img src={`/media/catalog/${d.image_kind}/orange/${d.id}.svg`} alt="" width={28} height={34} loading="lazy" />
                  </span>
                  <span>{d.name}</span>
                  <ChevronIcon size={18} className="drawer-chevron" />
                </button>
                <div className="drawer-children" hidden={!isOpen}>
                  <Link href={`/c/${d.slug}`} className="drawer-all">All {d.name}</Link>
                  {d.children.map((c) => (
                    <Link key={c.slug} href={`/c/${c.slug}`}>{c.name}</Link>
                  ))}
                </div>
              </div>
            )
          })}
          <Link href="/search?sort=discount&in_stock=true" className="drawer-row deals">Deals of the day</Link>
        </nav>
        <div className="drawer-links">
          <Link href="/wishlist"><HeartIcon size={20} /> Favorites</Link>
          <Link href="/cart"><CartIcon size={20} /> Cart</Link>
          {userName && <Link href="/account"><UserIcon size={20} /> Orders and account</Link>}
        </div>
      </aside>
    </>
  )
}
