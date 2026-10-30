import type { Metadata } from 'next'
import Link from 'next/link'
import { notFound } from 'next/navigation'
import { ProductListing } from '@/components/ProductListing'
import { findCategory, getCategories, search, type Params } from '@/lib/catalog'

type Props = { params: Promise<{ slug: string }>; searchParams: Promise<Params> }

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const found = await findCategory((await params).slug)
  if (!found) return {}
  const names = found.trail.map((c) => c.name)
  return {
    title: names.join(' › '),
    description: `Shop ${found.category.name.toLowerCase()} from hundreds of sellers on ReapSeek.`,
    alternates: { canonical: `/c/${found.category.slug}` },
  }
}

export default async function CategoryPage({ params, searchParams }: Props) {
  const found = await findCategory((await params).slug)
  if (!found) notFound()
  const query = await searchParams
  const listingParams: Params = { ...query, category: found.category.path }
  const [{ result, error }, tree] = await Promise.all([search(listingParams), getCategories()])
  return (
    <ProductListing
      basePath={`/c/${found.category.slug}`}
      params={listingParams}
      result={result}
      error={error}
      tree={tree}
      fixedCategory={found.category}
      heading={
        <>
          <nav className="breadcrumb" aria-label="Breadcrumb">
            <Link href="/">Home</Link>
            {found.trail.map((c) => (
              <span key={c.path} className="crumb">
                <span>/</span>
                {c.path === found.category.path ? <span>{c.name}</span> : <Link href={`/c/${c.slug}`}>{c.name}</Link>}
              </span>
            ))}
          </nav>
          <h1>{found.category.name}{typeof query.q === 'string' && query.q ? <span className="query"> matching &ldquo;{query.q}&rdquo;</span> : null}</h1>
        </>
      }
    />
  )
}
