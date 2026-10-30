import type { Metadata } from 'next'
import type { ReactNode } from 'react'
import '@fontsource-variable/inter'
import '@/styles/tokens.css'
import '@/styles/base.css'
import '@/styles/layout.css'
import '@/styles/components.css'
import { Footer } from '@/components/Footer'
import { Header } from '@/components/Header'
import { getCurrentUser } from '@/lib/session'

export const metadata: Metadata = {
  title: { default: 'ReapSeek: shop from hundreds of sellers', template: '%s | ReapSeek' },
  description: 'Fashion, electronics, home and more from hundreds of sellers, with fast delivery and easy returns.',
}

export default async function RootLayout({ children }: { children: ReactNode }) {
  const user = await getCurrentUser()
  return (
    <html lang="en">
      <body>
        <Header user={user} />
        <main className="site-main">{children}</main>
        <Footer />
      </body>
    </html>
  )
}
