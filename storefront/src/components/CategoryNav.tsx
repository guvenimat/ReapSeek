'use client'

import Link from 'next/link'
import { usePathname, useSearchParams } from 'next/navigation'
import { useEffect, useRef, useState } from 'react'
import type { CategoryNode, MenuInsight } from '@/lib/catalog'
import { ChevronIcon, DepartmentIcon, MenuIcon } from './icons'

const openDelay = 120
const closeDelay = 160
const all = '__all'

export function CategoryNav({ departments, insights }: { departments: CategoryNode[]; insights: Record<string, MenuInsight> }) {
  const [open, setOpen] = useState<string | null>(null)
  const [active, setActive] = useState(departments[0]?.slug ?? '')
  const timer = useRef<number | undefined>(undefined)
  const nav = useRef<HTMLElement>(null)
  const pathname = usePathname()
  const search = useSearchParams()

  const schedule = (key: string | null, delay: number) => {
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setOpen(key), delay)
  }
  const closeNow = () => {
    window.clearTimeout(timer.current)
    setOpen(null)
  }
  const focusFirst = (selector: string) =>
    requestAnimationFrame(() => nav.current?.querySelector<HTMLAnchorElement>(`${selector} a`)?.focus())

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

  const current = departments.find((d) => d.slug === active) ?? departments[0]
  const info = current ? insights[current.slug] : undefined

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
        <div className={`category-menu all-menu${open === all ? ' open' : ''}`} onMouseEnter={() => schedule(all, open ? 0 : openDelay)}>
          <button
            type="button"
            className="all-button"
            aria-expanded={open === all}
            aria-controls="all-categories"
            onClick={() => (open === all ? closeNow() : setOpen(all))}
            onKeyDown={(e) => {
              if (e.key === 'ArrowDown') {
                e.preventDefault()
                setOpen(all)
                focusFirst('#all-categories .flyout-side')
              }
            }}
          >
            <MenuIcon size={18} />
            All categories
          </button>
          {current && (
            <div className="flyout" id="all-categories" hidden={open !== all}>
              <div className="flyout-side">
                {departments.map((d) => (
                  <Link
                    key={d.slug}
                    href={`/c/${d.slug}`}
                    className={d.slug === current.slug ? 'active' : undefined}
                    onMouseEnter={() => setActive(d.slug)}
                    onFocus={() => setActive(d.slug)}
                    onClick={closeNow}
                  >
                    <DepartmentIcon slug={d.slug} size={20} />
                    <span>{d.name}</span>
                    <ChevronIcon size={14} className="flyout-chevron" />
                  </Link>
                ))}
              </div>
              <div className="flyout-main">
                <div className="flyout-head">
                  <h2>{current.name}</h2>
                  <Link href={`/c/${current.slug}`} onClick={closeNow}>
                    See all <ChevronIcon size={14} />
                  </Link>
                </div>
                <div className="flyout-subs">
                  {current.children.map((c) => (
                    <Link key={c.slug} href={`/c/${c.slug}`} onClick={closeNow}>{c.name}</Link>
                  ))}
                </div>
                {info && info.brands.length > 0 && (
                  <div className="flyout-brands">
                    <h3>Popular brands</h3>
                    <div>
                      {info.brands.map((b) => (
                        <Link key={b} href={`/c/${current.slug}?brand=${encodeURIComponent(b)}`} onClick={closeNow}>{b}</Link>
                      ))}
                    </div>
                  </div>
                )}
                <Link href={`/c/${current.slug}?sort=discount`} className="flyout-promo" onClick={closeNow}>
                  <img src={info?.promoImage ?? `/media/catalog/${current.image_kind}/orange/${current.id}.svg`} alt="" width={64} height={64} loading="lazy" />
                  <span>
                    <strong>{info && info.topDiscount > 0 ? `Up to ${info.topDiscount}% off in ${current.name}` : `Deals in ${current.name}`}</strong>
                    <small>Sellers compete, the best offer wins</small>
                  </span>
                </Link>
              </div>
            </div>
          )}
        </div>

        {departments.map((d) => {
          const isOpen = open === d.slug
          const hasChildren = d.children.length > 0
          return (
            <div
              key={d.slug}
              className={`category-menu quick${isOpen ? ' open' : ''}`}
              onMouseEnter={() => schedule(hasChildren ? d.slug : null, open ? 0 : openDelay)}
            >
              <Link
                href={`/c/${d.slug}`}
                className="category-link"
                aria-expanded={hasChildren ? isOpen : undefined}
                onClick={closeNow}
                onFocus={() => setOpen(hasChildren ? d.slug : null)}
                onKeyDown={(e) => {
                  if (e.key === 'ArrowDown' && hasChildren) {
                    e.preventDefault()
                    setOpen(d.slug)
                    focusFirst(`[data-menu="${d.slug}"]`)
                  }
                }}
              >
                {d.name}
              </Link>
              {hasChildren && (
                <div className="category-dropdown" data-menu={d.slug} hidden={!isOpen}>
                  {d.children.map((c) => (
                    <Link key={c.slug} href={`/c/${c.slug}`} onClick={closeNow}>{c.name}</Link>
                  ))}
                  <Link href={`/c/${d.slug}`} className="dropdown-all" onClick={closeNow}>
                    All {d.name} <ChevronIcon size={14} />
                  </Link>
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
