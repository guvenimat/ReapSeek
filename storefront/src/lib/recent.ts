'use client'

const key = 'rs_recently_viewed'
const max = 12

export function recentlyViewed(): number[] {
  try {
    const value = JSON.parse(window.localStorage.getItem(key) ?? '[]')
    return Array.isArray(value) ? value.filter((v): v is number => Number.isInteger(v) && v > 0).slice(0, max) : []
  } catch {
    return []
  }
}

export function rememberViewed(productId: number) {
  try {
    window.localStorage.setItem(key, JSON.stringify([productId, ...recentlyViewed().filter((id) => id !== productId)].slice(0, max)))
  } catch {
    return
  }
}
