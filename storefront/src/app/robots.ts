import type { MetadataRoute } from 'next'
import { publicUrl } from '@/lib/products'

export const dynamic = 'force-dynamic'

export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: '*', allow: '/', disallow: ['/account', '/login', '/register', '/search', '/api/'] },
    sitemap: `${publicUrl}/sitemap.xml`,
  }
}
