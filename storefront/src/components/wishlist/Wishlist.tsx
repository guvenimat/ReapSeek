'use client'

import { useRouter } from 'next/navigation'
import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { track } from '@/lib/analytics'
import { api } from '@/lib/client'
import { HeartIcon } from '../icons'

type WishlistState = { signedIn: boolean; ids: Set<number>; toggle: (productId: number, price: number | null) => Promise<void> }

const WishlistContext = createContext<WishlistState>({ signedIn: false, ids: new Set(), toggle: async () => {} })

export function WishlistProvider({ signedIn, children }: { signedIn: boolean; children: ReactNode }) {
  const router = useRouter()
  const [ids, setIds] = useState<Set<number>>(new Set())

  useEffect(() => {
    if (!signedIn) return
    api<{ product_ids: number[] }>('GET', '/wishlist/ids').then((r) => setIds(new Set(r.product_ids))).catch(() => {})
  }, [signedIn])

  const toggle = useCallback(async (productId: number, price: number | null) => {
    if (!signedIn) {
      router.push(`/login?next=${encodeURIComponent(window.location.pathname + window.location.search)}`)
      return
    }
    const saved = ids.has(productId)
    const r = saved
      ? await api<{ product_ids: number[] }>('DELETE', `/wishlist/${productId}`)
      : await api<{ product_ids: number[] }>('POST', '/wishlist', { product_id: productId })
    setIds(new Set(r.product_ids))
    track(saved ? 'wishlist_removed' : 'wishlist_added', { item_id: String(productId), price })
  }, [ids, signedIn, router])

  return <WishlistContext.Provider value={{ signedIn, ids, toggle }}>{children}</WishlistContext.Provider>
}

export const useWishlist = () => useContext(WishlistContext)

export function WishlistButton({ productId, price, large = false }: { productId: number; price: number | null; large?: boolean }) {
  const { ids, toggle } = useWishlist()
  const [busy, setBusy] = useState(false)
  const saved = ids.has(productId)
  const label = saved ? 'Remove from favorites' : 'Add to favorites'
  return (
    <button type="button" className={`wish-btn${saved ? ' saved' : ''}${large ? ' large' : ''}`} aria-label={label} title={label} aria-pressed={saved}
      data-testid="wishlist-button" disabled={busy}
      onClick={async (e) => {
        e.preventDefault()
        e.stopPropagation()
        setBusy(true)
        await toggle(productId, price).catch(() => undefined)
        setBusy(false)
      }}>
      <HeartIcon size={large ? 20 : 18} fill={saved ? 'currentColor' : 'none'} />
      {large && <span>{saved ? 'Saved' : 'Save'}</span>}
    </button>
  )
}
