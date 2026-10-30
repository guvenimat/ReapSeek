'use client'

import type { ReapDeep } from '@reapdeep/js'

export type AnalyticsConfig = { publicKey: string; apiHost: string; recommendHost: string | null }

type Call = (rd: ReapDeep) => void

const userKey = 'reapseek.analytics.user'
let client: ReapDeep | null = null
let starting: Promise<ReapDeep | null> | null = null
const pending: Call[] = []

function run(call: Call) {
  if (client) call(client)
  else pending.push(call)
}

export function startAnalytics(config: AnalyticsConfig): Promise<ReapDeep | null> {
  starting ??= import('@reapdeep/js').then(({ init }) => {
    client = init(config.publicKey, { apiHost: config.apiHost, recommendHost: config.recommendHost ?? undefined })
    for (const call of pending.splice(0)) call(client)
    return client
  }).catch(() => null)
  return starting
}

export function track(event: string, properties: Record<string, unknown> = {}) {
  run((rd) => rd.capture(event, properties))
}

export function customerDistinctId(userId: number) {
  return `customer-${userId}`
}

export function syncIdentity(userId: number | null) {
  run((rd) => {
    let stored: string | null = null
    try {
      stored = localStorage.getItem(userKey)
    } catch {
      stored = null
    }
    const wanted = userId === null ? null : customerDistinctId(userId)
    if (wanted === stored && (wanted === null || rd.getDistinctId() === wanted)) return
    if (stored && stored !== wanted) rd.reset()
    if (wanted) rd.identify(wanted, { customer: true })
    try {
      if (wanted) localStorage.setItem(userKey, wanted)
      else localStorage.removeItem(userKey)
    } catch {
      return
    }
  })
}
