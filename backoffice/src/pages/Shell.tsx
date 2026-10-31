import type { ReactNode } from 'react'
import { api, type User } from '../api'
import {
  BoxIcon, ChatIcon, HomeIcon, ReturnIcon, StarIcon, LayersIcon, LightbulbIcon, LogOutIcon, LogoMark, MegaphoneIcon, ReceiptIcon, StoreIcon, TagIcon, TruckIcon, UsersIcon, WalletIcon,
} from '../components/icons'
import { useRoute, type Route } from '../lib'
import { Categories } from './Categories'
import { OrderDetailPage, Orders } from './Orders'
import { Overview } from './Overview'
import { Moderation, SellerQuestions } from './Moderation'
import { Payouts } from './Payouts'
import { ProductDetailPage } from './ProductDetail'
import { ProductNew } from './ProductNew'
import { Products } from './Products'
import { SellerCatalog, SellerHome, SellerOffers, SellerPackages, SellerProduct, SellerProposals, SellerReturns } from './SellerPanel'
import { Sellers } from './Sellers'

type Section = { key: string; label: string; icon: () => ReactNode; ready: boolean }

const adminSections: Section[] = [
  { key: '', label: 'Overview', icon: HomeIcon, ready: true },
  { key: 'products', label: 'Products', icon: BoxIcon, ready: true },
  { key: 'categories', label: 'Categories', icon: LayersIcon, ready: true },
  { key: 'sellers', label: 'Sellers', icon: StoreIcon, ready: true },
  { key: 'orders', label: 'Orders', icon: ReceiptIcon, ready: true },
  { key: 'payouts', label: 'Payouts', icon: WalletIcon, ready: true },
  { key: 'moderation', label: 'Reviews', icon: StarIcon, ready: true },
  { key: 'customers', label: 'Customers', icon: UsersIcon, ready: false },
  { key: 'marketing', label: 'Marketing', icon: MegaphoneIcon, ready: false },
]

const sellerSections: Section[] = [
  { key: '', label: 'Earnings', icon: WalletIcon, ready: true },
  { key: 'packages', label: 'Packages', icon: TruckIcon, ready: true },
  { key: 'returns', label: 'Returns', icon: ReturnIcon, ready: true },
  { key: 'questions', label: 'Questions', icon: ChatIcon, ready: true },
  { key: 'offers', label: 'Offers', icon: TagIcon, ready: true },
  { key: 'catalog', label: 'Catalog', icon: BoxIcon, ready: true },
  { key: 'proposals', label: 'Proposals', icon: LightbulbIcon, ready: true },
]

function adminPage(section: Section, route: Route, user: User): [ReactNode, string] {
  const [, sub] = route.path
  if (section.key === 'products' && sub === 'new') return [<ProductNew />, 'New product']
  if (section.key === 'products' && sub) return [<ProductDetailPage id={Number(sub)} />, 'Product']
  if (section.key === 'orders' && sub) return [<OrderDetailPage number={sub} />, 'Order']
  if (section.key === 'orders') return [<Orders query={route.query} />, section.label]
  if (section.key === 'products') return [<Products query={route.query} />, section.label]
  if (section.key === 'categories') return [<Categories />, section.label]
  if (section.key === 'sellers') return [<Sellers />, section.label]
  if (section.key === 'payouts') return [<Payouts />, section.label]
  if (section.key === 'moderation') return [<Moderation query={route.query} />, section.label]
  return [<Overview user={user} />, section.label]
}

function sellerPage(section: Section, route: Route, user: User): [ReactNode, string] {
  const [, sub] = route.path
  if (section.key === 'catalog' && sub === 'new') return [<ProductNew seller />, 'Propose a product']
  if (section.key === 'catalog' && sub) return [<SellerProduct id={Number(sub)} />, 'Product']
  if (section.key === 'catalog') return [<SellerCatalog query={route.query} />, section.label]
  if (section.key === 'packages') return [<SellerPackages query={route.query} />, section.label]
  if (section.key === 'offers') return [<SellerOffers query={route.query} />, section.label]
  if (section.key === 'proposals') return [<SellerProposals />, section.label]
  if (section.key === 'returns') return [<SellerReturns query={route.query} />, section.label]
  if (section.key === 'questions') return [<SellerQuestions query={route.query} />, section.label]
  return [<SellerHome user={user} />, section.label]
}

export function Shell({ user, onSignedOut }: { user: User; onSignedOut: () => void }) {
  const route = useRoute()
  const isSeller = user.role === 'seller'
  const sections = isSeller ? sellerSections : adminSections
  const current = route.path[0] ?? ''
  const section = sections.find((s) => s.key === current && s.ready) ?? sections[0]
  const [page, title] = isSeller ? sellerPage(section, route, user) : adminPage(section, route, user)

  async function signOut() {
    await api.logout().catch(() => undefined)
    onSignedOut()
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <LogoMark />
          <div>
            <strong>ReapSeek</strong>
            <span>{isSeller ? 'Seller center' : 'Back office'}</span>
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
