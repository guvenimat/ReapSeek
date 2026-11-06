import type { Metadata } from 'next'
import type { ReactNode } from 'react'
import '@fontsource-variable/plus-jakarta-sans'
import '@/styles/tokens.css'
import '@/styles/base.css'
import '@/styles/layout.css'
import '@/styles/components.css'
import '@/styles/listing.css'
import '@/styles/product.css'
import '@/styles/orders.css'
import '@/styles/wishlist.css'
import { Analytics } from '@/components/analytics/Analytics'
import { Footer } from '@/components/Footer'
import { Header } from '@/components/Header'
import { WishlistProvider } from '@/components/wishlist/Wishlist'
import { getCategories, getMenuInsights } from '@/lib/catalog'
import { getCartCount } from '@/lib/orders'
import { publicUrl } from '@/lib/products'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = {
  metadataBase: new URL(publicUrl),
  title: { default: 'ReapSeek: shop from hundreds of sellers', template: '%s | ReapSeek' },
  description: 'Fashion, electronics, home and more from hundreds of sellers, with fast delivery and easy returns.',
}

export default async function RootLayout({ children }: { children: ReactNode }) {
  const [user, departments, cartCount] = await Promise.all([getCurrentUser(), getCategories(), getCartCount()])
  const insights = await getMenuInsights(departments)
  const analytics = process.env.REAPDEEP_PUBLIC_KEY && process.env.REAPDEEP_INGEST_URL
    ? { publicKey: process.env.REAPDEEP_PUBLIC_KEY, apiHost: process.env.REAPDEEP_INGEST_URL, recommendHost: process.env.REAPDEEP_RECOMMEND_URL ?? null }
    : null
  return (
    <html lang="en">
      <body>
        <Analytics config={analytics} userId={user?.id ?? null} />
        <WishlistProvider signedIn={user !== null}>
          <Header user={user} departments={departments} insights={insights} cartCount={cartCount} />
          <main className="site-main">{children}</main>
        </WishlistProvider>
        <Footer departments={departments} />
      </body>
    </html>
  )
}
