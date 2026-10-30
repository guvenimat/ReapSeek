import { useCallback, useEffect, useState } from 'react'

export type Route = { path: string[]; query: URLSearchParams }

function parse(): Route {
  const raw = window.location.hash.replace(/^#\/?/, '')
  const [path, query = ''] = raw.split('?')
  return { path: path.split('/').filter(Boolean), query: new URLSearchParams(query) }
}

export function useRoute(): Route {
  const [route, setRoute] = useState(parse)
  useEffect(() => {
    const onChange = () => setRoute(parse())
    window.addEventListener('hashchange', onChange)
    return () => window.removeEventListener('hashchange', onChange)
  }, [])
  return route
}

export function navigate(path: string, query?: Record<string, string | number | undefined>) {
  const params = new URLSearchParams()
  for (const [k, v] of Object.entries(query ?? {})) if (v !== undefined && v !== '') params.set(k, String(v))
  const q = params.toString()
  window.location.hash = `/${path}${q ? `?${q}` : ''}`
}

export type Loadable<T> = { data?: T; error?: string; loading: boolean; reload: () => void }

export function useLoad<T>(load: () => Promise<T>, deps: unknown[]): Loadable<T> {
  const [state, setState] = useState<{ data?: T; error?: string; loading: boolean }>({ loading: true })
  const [version, setVersion] = useState(0)
  useEffect(() => {
    let cancelled = false
    setState((s) => ({ ...s, loading: true, error: undefined }))
    load().then(
      (data) => !cancelled && setState({ data, loading: false }),
      (err: Error) => !cancelled && setState({ error: err.message, loading: false }),
    )
    return () => {
      cancelled = true
    }
  }, [...deps, version])
  const reload = useCallback(() => setVersion((v) => v + 1), [])
  return { ...state, reload }
}

export function useAction() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const run = async <T,>(action: () => Promise<T>): Promise<T | undefined> => {
    setBusy(true)
    setError(undefined)
    try {
      return await action()
    } catch (err) {
      setError((err as Error).message)
      return undefined
    } finally {
      setBusy(false)
    }
  }
  return { busy, error, setError, run }
}

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'TRY', currencyDisplay: 'narrowSymbol' })
const count = new Intl.NumberFormat('en-US')
const date = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' })

export const formatMoney = (value: number | null | undefined) => (value == null ? '-' : money.format(value))
export const formatCount = (value: number) => count.format(value)
export const formatDate = (value: string) => date.format(new Date(value))
export const formatOptions = (options: Record<string, string>) =>
  Object.keys(options).length === 0 ? 'Default' : Object.entries(options).map(([k, v]) => `${k}: ${v}`).join(' · ')
