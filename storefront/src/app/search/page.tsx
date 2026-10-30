import type { Metadata } from 'next'
import { ProductListing } from '@/components/ProductListing'
import { getCategories, search, type Params } from '@/lib/catalog'

type Props = { searchParams: Promise<Params> }

const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v)

export async function generateMetadata({ searchParams }: Props): Promise<Metadata> {
  const q = first((await searchParams).q)
  return { title: q ? `"${q}"` : 'All products', robots: { index: false } }
}

export default async function SearchPage({ searchParams }: Props) {
  const params = await searchParams
  const [{ result, error }, tree] = await Promise.all([search(params), getCategories()])
  const q = first(params.q)
  return (
    <ProductListing
      basePath="/search"
      params={params}
      result={result}
      error={error}
      tree={tree}
      heading={q ? <h1>Results for <span className="query">&ldquo;{q}&rdquo;</span></h1> : <h1>All products</h1>}
    />
  )
}
