import type { ReactNode } from 'react'
import { api, type User } from '../api'
import { BoxIcon, HomeIcon, LayersIcon, LogOutIcon, LogoMark, MegaphoneIcon, ReceiptIcon, StoreIcon, UsersIcon } from '../components/icons'
import { useRoute } from '../lib'
import { Categories } from './Categories'
import { Overview } from './Overview'
import { ProductDetailPage } from './ProductDetail'
import { ProductNew } from './ProductNew'
import { Products } from './Products'
import { Sellers } from './Sellers'

type Section = { key: string; label: string; icon: () => ReactNode; ready: boolean }

const sections: Section[] = [
  { key: '', label: 'Overview', icon: HomeIcon, ready: true },
  { key: 'products', label: 'Products', icon: BoxIcon, ready: true },
  { key: 'categories', label: 'Categories', icon: LayersIcon, ready: true },
  { key: 'sellers', label: 'Sellers', icon: StoreIcon, ready: true },
  { key: 'orders', label: 'Orders', icon: ReceiptIcon, ready: false },
  { key: 'customers', label: 'Customers', icon: UsersIcon, ready: false },
  { key: 'marketing', label: 'Marketing', icon: MegaphoneIcon, ready: false },
]

export function Shell({ user, onSignedOut }: { user: User; onSignedOut: () => void }) {
  const route = useRoute()
  const current = route.path[0] ?? ''
  const section = sections.find((s) => s.key === current && s.ready) ?? sections[0]

  async function signOut() {
    await api.logout().catch(() => undefined)
    onSignedOut()
  }

  let page: ReactNode
  let title = section.label
  if (section.key === 'products' && route.path[1] === 'new') {
    page = <ProductNew />
    title = 'New product'
  } else if (section.key === 'products' && route.path[1]) {
    page = <ProductDetailPage id={Number(route.path[1])} />
    title = 'Product'
  } else if (section.key === 'products') page = <Products query={route.query} />
  else if (section.key === 'categories') page = <Categories />
  else if (section.key === 'sellers') page = <Sellers />
  else page = <Overview user={user} />

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <LogoMark />
          <div>
            <strong>ReapSeek</strong>
            <span>Back office</span>
          </div>
        </div>
        <nav aria-label="Sections">
          {sections.map(({ key, label, icon: IconComponent, ready }) =>
            ready ? (
              <a key={label} href={`#/${key}`} className={`nav-item${key === section.key ? ' active' : ''}`} data-nav={key || 'overview'}>
                <IconComponent />
                {label}
              </a>
            ) : (
              <span key={label} className="nav-item disabled" aria-disabled="true">
                <IconComponent />
                {label}
                <em>Soon</em>
              </span>
            ),
          )}
        </nav>
      </aside>
      <div className="content">
        <header className="topbar">
          <h1>{title}</h1>
          <div className="topbar-user">
            <div className="who">
              <strong data-testid="user-name">{user.name}</strong>
              <span>{user.email}</span>
            </div>
            <button type="button" className="btn btn-ghost" onClick={signOut} data-testid="sign-out">
              <LogOutIcon /> Sign out
            </button>
          </div>
        </header>
        <main className="page">{page}</main>
      </div>
    </div>
  )
}
