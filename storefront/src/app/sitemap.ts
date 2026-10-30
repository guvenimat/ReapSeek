import type { MetadataRoute } from 'next'
import { getSitemap, publicUrl } from '@/lib/products'

export const dynamic = 'force-dynamic'

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const { products, categories } = await getSitemap()
  return [
    { url: `${publicUrl}/`, changeFrequency: 'daily', priority: 1 },
    ...categories.map((slug) => ({ url: `${publicUrl}/c/${slug}`, changeFrequency: 'daily' as const, priority: 0.8 })),
    ...products.map((p) => ({ url: `${publicUrl}/p/${p.slug}`, lastModified: new Date(p.updated_at), changeFrequency: 'weekly' as const, priority: 0.6 })),
  ]
}
