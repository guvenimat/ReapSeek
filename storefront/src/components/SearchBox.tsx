'use client'

import Link from 'next/link'
import { useRouter, useSearchParams } from 'next/navigation'
import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { formatPrice, type FacetValue, type ProductCard } from '@/lib/catalog'
import { SearchIcon } from './icons'

type Suggestions = { query: string; products: ProductCard[]; brands: FacetValue[]; categories: FacetValue[] }
type Item = { key: string; href: string }

export function SearchBox() {
  const router = useRouter()
  const params = useSearchParams()
  const [q, setQ] = useState(params.get('q') ?? '')
  const [data, setData] = useState<Suggestions | null>(null)
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const box = useRef<HTMLFormElement>(null)

  useEffect(() => setQ(params.get('q') ?? ''), [params])

  useEffect(() => {
    const term = q.trim()
    if (term.length < 2) {
      setData(null)
      return
    }
    const controller = new AbortController()
    const timer = setTimeout(() => {
      fetch(`/api/v1/search/suggest?q=${encodeURIComponent(term)}`, { signal: controller.signal })
        .then((r) => (r.ok ? r.json() : null))
        .then((d: Suggestions | null) => {
          setData(d)
          setActive(-1)
        })
        .catch(() => undefined)
    }, 150)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [q])

  useEffect(() => {
    const close = (e: MouseEvent) => {
      if (box.current && !box.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [])

  const items: Item[] = data
    ? [
        ...data.categories.map((c) => ({ key: `c:${c.value}`, href: `/c/${c.value.split('/').pop()}` })),
        ...data.brands.map((b) => ({ key: `b:${b.value}`, href: `/search?brand=${encodeURIComponent(b.value)}` })),
        ...data.products.map((p) => ({ key: `p:${p.id}`, href: `/p/${p.slug}` })),
      ]
    : []

  const go = (href: string) => {
    setOpen(false)
    router.push(href)
  }
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (active >= 0 && items[active]) return go(items[active].href)
    const term = q.trim()
    if (term) go(`/search?q=${encodeURIComponent(term)}`)
  }
  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setOpen(true)
      setActive((i) => Math.min(i + 1, items.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((i) => Math.max(i - 1, -1))
    } else if (e.key === 'Escape') {
      setOpen(false)
    }
  }
  const index = (key: string) => items.findIndex((i) => i.key === key)
  const show = open && data !== null && items.length > 0

  return (
    <form className="search" role="search" action="/search" onSubmit={submit} ref={box}>
      <SearchIcon size={18} className="search-icon" />
      <input
        type="search" name="q" value={q} autoComplete="off" maxLength={100}
        placeholder="Search for products, brands and categories" aria-label="Search"
        aria-expanded={show} aria-controls="search-suggestions"
        onChange={(e) => {
          setQ(e.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
        onKeyDown={onKey}
        data-testid="search-input"
      />
      <button type="submit">Search</button>
      {show && (
        <div className="suggestions" id="search-suggestions" role="listbox" data-testid="suggestions">
          {data.categories.length > 0 && (
            <div className="suggestion-group">
              <span className="suggestion-title">Categories</span>
              {data.categories.map((c) => (
                <Link key={c.value} href={items[index(`c:${c.value}`)].href} onClick={() => setOpen(false)}
                  className={`suggestion${active === index(`c:${c.value}`) ? ' active' : ''}`}>
                  <span>{c.label}</span>
                  <span className="muted">{c.count}</span>
                </Link>
              ))}
            </div>
          )}
          {data.brands.length > 0 && (
            <div className="suggestion-group">
              <span className="suggestion-title">Brands</span>
              {data.brands.map((b) => (
                <Link key={b.value} href={items[index(`b:${b.value}`)].href} onClick={() => setOpen(false)}
                  className={`suggestion${active === index(`b:${b.value}`) ? ' active' : ''}`}>
                  <span>{b.label}</span>
                </Link>
              ))}
            </div>
          )}
          <div className="suggestion-group">
            <span className="suggestion-title">Products</span>
            {data.products.map((p) => (
              <Link key={p.id} href={`/p/${p.slug}`} onClick={() => setOpen(false)}
                className={`suggestion product${active === index(`p:${p.id}`) ? ' active' : ''}`}>
                {p.image_url && <img src={p.image_url} alt="" width={32} height={40} />}
                <span className="suggestion-name">{p.title}</span>
                {p.price !== null && <span className="suggestion-price">{formatPrice(p.price)}</span>}
              </Link>
            ))}
          </div>
        </div>
      )}
    </form>
  )
}
