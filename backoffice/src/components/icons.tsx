import type { ReactNode } from 'react'

function Icon({ size = 18, children }: { size?: number; children: ReactNode }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

export const HomeIcon = () => <Icon><path d="M4 11 12 4l8 7" /><path d="M6 10v10h12V10" /></Icon>
export const BoxIcon = () => <Icon><path d="M3.5 7.5 12 3l8.5 4.5v9L12 21l-8.5-4.5z" /><path d="M3.5 7.5 12 12l8.5-4.5M12 12v9" /></Icon>
export const StoreIcon = () => <Icon><path d="M4 10v10h16V10" /><path d="M3 10 5 4h14l2 6" /><path d="M10 20v-5h4v5" /></Icon>
export const ReceiptIcon = () => <Icon><path d="M6 3h12v18l-3-2-3 2-3-2-3 2z" /><path d="M9 8h6M9 12h6" /></Icon>
export const UsersIcon = () => <Icon><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6" /><path d="M16 4.5a3.5 3.5 0 0 1 0 7M18 14c2.2.6 3.5 2.9 3.5 6" /></Icon>
export const MegaphoneIcon = () => <Icon><path d="M4 10v4h3l7 4V6L7 10z" /><path d="M18 9a4 4 0 0 1 0 6" /></Icon>
export const LayersIcon = () => <Icon><path d="M12 3 3 8l9 5 9-5z" /><path d="m3 13 9 5 9-5" /></Icon>
export const PlusIcon = () => <Icon><path d="M12 5v14M5 12h14" /></Icon>
export const TrashIcon = () => <Icon><path d="M4 7h16M10 11v6M14 11v6" /><path d="M6 7l1 13h10l1-13M9 7V4h6v3" /></Icon>
export const LogOutIcon = () => <Icon><path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3" /><path d="M10 16l-4-4 4-4M6 12h10" /></Icon>

export const LogoMark = ({ size = 30 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
    <rect width="32" height="32" rx="9" fill="#f2542d" />
    <circle cx="14.5" cy="14.5" r="6.5" fill="none" stroke="#fff" strokeWidth="3" />
    <path d="m19.5 19.5 5 5" stroke="#16181d" strokeWidth="3.4" strokeLinecap="round" />
  </svg>
)
export const TruckIcon = () => <Icon><path d="M3 6h11v10H3zM14 10h4l3 3v3h-7" /><circle cx="7" cy="18" r="1.8" /><circle cx="17" cy="18" r="1.8" /></Icon>
export const WalletIcon = () => <Icon><path d="M4 7h14a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4z" /><path d="M4 7V5.5A1.5 1.5 0 0 1 5.5 4H16v3" /><path d="M16 13.5h2" /></Icon>
export const TagIcon = () => <Icon><path d="M3 12V4h8l9 9-8 8z" /><circle cx="7.5" cy="8.5" r="1.3" /></Icon>
export const LightbulbIcon = () => <Icon><path d="M9 18h6M10 21h4" /><path d="M12 3a6 6 0 0 0-3.5 10.9V16h7v-2.1A6 6 0 0 0 12 3z" /></Icon>
export const ReturnIcon = () => <Icon><path d="M9 14 4 9l5-5" /><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11" /></Icon>
export const StarIcon = () => <Icon><path d="m12 3.5 2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.9l-5.2 2.7 1-5.8-4.3-4.1 5.9-.9z" /></Icon>
export const ChatIcon = () => <Icon><path d="M4 5h16v11H9l-5 4z" /><path d="M8 10h8M8 13h5" /></Icon>
export const BellIcon = () => <Icon><path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z" /><path d="M10 20a2 2 0 0 0 4 0" /></Icon>
export const GearIcon = () => <Icon><circle cx="12" cy="12" r="3" /><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1" /></Icon>
export const MenuIcon = () => <Icon size={22}><path d="M4 7h16M4 12h16M4 17h16" /></Icon>
export const CloseIcon = () => <Icon size={20}><path d="M6 6l12 12M18 6 6 18" /></Icon>
