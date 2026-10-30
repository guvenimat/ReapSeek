import type { Metadata } from 'next'
import type { ReactNode } from 'react'
import '@fontsource-variable/inter'
import '@/styles/tokens.css'
import '@/styles/base.css'
import '@/styles/layout.css'
import '@/styles/components.css'
import '@/styles/listing.css'
import '@/styles/product.css'
import { Analytics } from '@/components/analytics/Analytics'
import { Footer } from '@/components/Footer'
import { Header } from '@/components/Header'
import { getCategories } from '@/lib/catalog'
import { publicUrl } from '@/lib/products'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = {
  metadataBase: new URL(publicUrl),
  title: { default: 'ReapSeek: shop from hundreds of sellers', template: '%s | ReapSeek' },
  description: 'Fashion, electronics, home and more from hundreds of sellers, with fast delivery and easy returns.',
}

export default async function RootLayout({ children }: { children: ReactNode }) {
  const [user, departments] = await Promise.all([getCurrentUser(), getCategories()])
  const analytics = process.env.REAPDEEP_PUBLIC_KEY && process.env.REAPDEEP_INGEST_URL
    ? { publicKey: process.env.REAPDEEP_PUBLIC_KEY, apiHost: process.env.REAPDEEP_INGEST_URL, recommendHost: process.env.REAPDEEP_RECOMMEND_URL ?? null }
    : null
  return (
    <html lang="en">
      <body>
        <Analytics config={analytics} userId={user?.id ?? null} />
        <Header user={user} departments={departments} />
        <main className="site-main">{children}</main>
        <Footer />
      </body>
    </html>
  )
}
