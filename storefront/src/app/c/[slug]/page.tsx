import type { Metadata } from 'next'
import Link from 'next/link'
import { notFound } from 'next/navigation'
import { categories, findCategory } from '@/lib/categories'

type Props = { params: Promise<{ slug: string }> }

export function generateStaticParams() {
  return categories.map((c) => ({ slug: c.slug }))
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const category = findCategory((await params).slug)
  return category ? { title: category.name, description: `${category.name}: ${category.tagline}` } : {}
}

export default async function CategoryPage({ params }: Props) {
  const category = findCategory((await params).slug)
  if (!category) notFound()
  return (
    <div className="container">
      <nav className="breadcrumb" aria-label="Breadcrumb">
        <Link href="/">Home</Link>
        <span>/</span>
        <span>{category.name}</span>
      </nav>
      <section className="category-hero" style={{ ['--hue' as string]: category.hue }}>
        <h1>{category.name}</h1>
        <p>{category.tagline}</p>
      </section>
      <div className="empty-state" data-testid="category-empty">
        <strong>Products are on their way</strong>
        <span>Sellers are listing their {category.name.toLowerCase()} products. Check back soon.</span>
      </div>
    </div>
  )
}
