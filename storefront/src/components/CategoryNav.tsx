'use client'

import Link from 'next/link'
import { usePathname, useSearchParams } from 'next/navigation'
import { useEffect, useRef, useState } from 'react'
import type { CategoryNode } from '@/lib/catalog'
import { ChevronIcon } from './icons'

const openDelay = 120
const closeDelay = 160

export function CategoryNav({ departments }: { departments: CategoryNode[] }) {
  const [open, setOpen] = useState<string | null>(null)
  const timer = useRef<number | undefined>(undefined)
  const nav = useRef<HTMLElement>(null)
  const pathname = usePathname()
  const search = useSearchParams()

  const schedule = (slug: string | null, delay: number) => {
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setOpen(slug), delay)
  }
  const closeNow = () => {
    window.clearTimeout(timer.current)
    setOpen(null)
  }

  useEffect(() => closeNow(), [pathname, search])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeNow()
    }
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      window.clearTimeout(timer.current)
    }
  }, [])

  return (
    <nav
      className="category-bar"
      aria-label="Categories"
      ref={nav}
      onMouseLeave={() => schedule(null, closeDelay)}
      onBlur={(e) => {
        if (!nav.current?.contains(e.relatedTarget as Node | null)) closeNow()
      }}
    >
      <div className="container category-inner">
        {departments.map((d) => {
          const isOpen = open === d.slug
          return (
            <div
              key={d.slug}
              className={`category-menu${isOpen ? ' open' : ''}`}
              onMouseEnter={() => schedule(d.children.length > 0 ? d.slug : null, open ? 0 : openDelay)}
            >
              <Link
                href={`/c/${d.slug}`}
                className="category-link"
                aria-expanded={d.children.length > 0 ? isOpen : undefined}
                onClick={closeNow}
                onFocus={() => setOpen(d.children.length > 0 ? d.slug : null)}
                onKeyDown={(e) => {
                  if (e.key === 'ArrowDown' && d.children.length > 0) {
                    e.preventDefault()
                    setOpen(d.slug)
                    requestAnimationFrame(() => nav.current?.querySelector<HTMLAnchorElement>(`[data-menu="${d.slug}"] a`)?.focus())
                  }
                }}
              >
                {d.name}
              </Link>
              {d.children.length > 0 && (
                <div className="category-dropdown" data-menu={d.slug} hidden={!isOpen}>
                  <div className="container mega">
                    <div className="mega-links">
                      <Link href={`/c/${d.slug}`} className="mega-all" onClick={closeNow}>
                        All {d.name} <ChevronIcon size={16} />
                      </Link>
                      <div className="mega-grid">
                        {d.children.map((c) => (
                          <Link key={c.slug} href={`/c/${c.slug}`} onClick={closeNow}>{c.name}</Link>
                        ))}
                      </div>
                    </div>
                    <Link href={`/c/${d.slug}?sort=discount`} className="mega-promo" onClick={closeNow}>
                      <span className="mega-art" aria-hidden="true">
                        <img src={`/media/catalog/${d.image_kind}/orange/${d.id}.svg`} alt="" width={120} height={150} loading="lazy" />
                      </span>
                      <span>
                        <small>Deals in {d.name}</small>
                        <strong>Best offers from every seller</strong>
                      </span>
                    </Link>
                  </div>
                </div>
              )}
            </div>
          )
        })}
        <Link href="/search?sort=discount&in_stock=true" className="category-link deals">Deals</Link>
      </div>
    </nav>
  )
}
