'use client'

import { useEffect, useRef } from 'react'
import { startAnalytics, syncIdentity, track, type AnalyticsConfig } from '@/lib/analytics'

export function Analytics({ config, userId }: { config: AnalyticsConfig | null; userId: number | null }) {
  useEffect(() => {
    if (config) void startAnalytics(config)
  }, [config])

  useEffect(() => {
    if (config) syncIdentity(userId)
  }, [config, userId])

  return null
}

export function TrackEvent({ event, properties, onceKey }: { event: string; properties: Record<string, unknown>; onceKey: string }) {
  const sent = useRef<string | null>(null)
  useEffect(() => {
    if (sent.current === onceKey) return
    sent.current = onceKey
    track(event, properties)
  }, [event, properties, onceKey])
  return null
}
