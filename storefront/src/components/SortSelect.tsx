'use client'

import { usePathname, useRouter, useSearchParams } from 'next/navigation'

const options = [
  { value: 'relevance', label: 'Recommended' },
  { value: 'price_asc', label: 'Price: low to high' },
  { value: 'price_desc', label: 'Price: high to low' },
  { value: 'discount', label: 'Biggest discount' },
  { value: 'rating', label: 'Top rated' },
  { value: 'newest', label: 'Newest' },
]

export function SortSelect({ value, hasQuery }: { value: string; hasQuery: boolean }) {
  const router = useRouter()
  const pathname = usePathname()
  const params = useSearchParams()

  const change = (sort: string) => {
    const next = new URLSearchParams(params.toString())
    next.delete('page')
    if (sort === 'relevance') next.delete('sort')
    else next.set('sort', sort)
    const qs = next.toString()
    router.push(`${pathname}${qs ? `?${qs}` : ''}`)
  }

  return (
    <label className="sort">
      <span>Sort by</span>
      <select value={value} onChange={(e) => change(e.target.value)} data-testid="sort">
        {options.map((o) => (
          <option key={o.value} value={o.value}>{o.value === 'relevance' && hasQuery ? 'Best match' : o.label}</option>
        ))}
      </select>
    </label>
  )
}
