import type { ReactNode, SVGProps } from 'react'

type IconProps = SVGProps<SVGSVGElement> & { size?: number }

function Icon({ size = 20, children, ...rest }: IconProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" {...rest}>
      {children}
    </svg>
  )
}

export const SearchIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" /></Icon>
)

export const UserIcon = (p: IconProps) => (
  <Icon {...p}><circle cx="12" cy="8" r="4" /><path d="M4 21c0-4 3.6-7 8-7s8 3 8 7" /></Icon>
)

export const HeartIcon = (p: IconProps) => (
  <Icon {...p}><path d="M12 20s-7.5-4.6-9.3-9.2C1.5 7.6 3.6 4.5 7 4.5c2 0 3.4 1.1 5 3 1.6-1.9 3-3 5-3 3.4 0 5.5 3.1 4.3 6.3C19.5 15.4 12 20 12 20Z" /></Icon>
)

export const CartIcon = (p: IconProps) => (
  <Icon {...p}><path d="M3 4h2.2l2.3 11.2a1.5 1.5 0 0 0 1.5 1.2h8.6a1.5 1.5 0 0 0 1.5-1.1L21 8H6.1" /><circle cx="9.5" cy="20" r="1.3" /><circle cx="17.5" cy="20" r="1.3" /></Icon>
)

export const TruckIcon = (p: IconProps) => (
  <Icon {...p}><path d="M2.5 6.5h11v9h-11z" /><path d="M13.5 9.5h4l3 3.2v2.8h-7" /><circle cx="6.5" cy="17.5" r="1.8" /><circle cx="17" cy="17.5" r="1.8" /></Icon>
)

export const ShieldIcon = (p: IconProps) => (
  <Icon {...p}><path d="M12 3 4.5 6v5.5c0 4.6 3.2 8.2 7.5 9.5 4.3-1.3 7.5-4.9 7.5-9.5V6L12 3Z" /><path d="m8.8 12 2.2 2.2 4.2-4.4" /></Icon>
)

export const ReturnIcon = (p: IconProps) => (
  <Icon {...p}><path d="M9 14 4 9l5-5" /><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11" /></Icon>
)

export const StoreIcon = (p: IconProps) => (
  <Icon {...p}><path d="M4 10v10h16V10" /><path d="M3 10 5 4h14l2 6c0 1.4-1.1 2.5-2.5 2.5S16 11.4 16 10c0 1.4-1.1 2.5-2.5 2.5h-3C9.1 12.5 8 11.4 8 10c0 1.4-1.1 2.5-2.5 2.5S3 11.4 3 10Z" /><path d="M10 20v-4.5h4V20" /></Icon>
)

export const MenuIcon = (p: IconProps) => (
  <Icon {...p}><path d="M4 7h16M4 12h16M4 17h16" /></Icon>
)

export const CloseIcon = (p: IconProps) => (
  <Icon {...p}><path d="M6 6l12 12M18 6 6 18" /></Icon>
)

export const ChevronIcon = (p: IconProps) => (
  <Icon {...p}><path d="m9 6 6 6-6 6" /></Icon>
)

export const BoltIcon = (p: IconProps) => (
  <Icon {...p}><path d="M13 3 5 13.5h6L10 21l8-10.5h-6L13 3Z" /></Icon>
)

export const LogoMark = ({ size = 32 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
    <rect width="32" height="32" rx="9" fill="var(--brand)" />
    <circle cx="14.5" cy="14.5" r="6.5" fill="none" stroke="#fff" strokeWidth="3" />
    <path d="m19.5 19.5 5 5" stroke="var(--accent)" strokeWidth="3.4" strokeLinecap="round" />
  </svg>
)

const departmentPaths: Record<string, ReactNode> = {
  women: <><circle cx="12" cy="5.5" r="2.5" /><path d="M9.5 10h5l2.5 10h-10l2.5-10Z" /></>,
  men: <path d="M8 4h8l3.5 4-3 2V20h-9V10l-3-2L8 4Z" />,
  kids: <><circle cx="12" cy="9" r="4" /><path d="M6 21c0-3.5 2.7-6 6-6s6 2.5 6 6" /></>,
  home: <><path d="M4 11 12 4l8 7v9H4v-9Z" /><path d="M10 20v-5h4v5" /></>,
  electronics: <><rect x="7" y="3" width="10" height="18" rx="2" /><path d="M11 18h2" /></>,
  beauty: <><path d="M10 3h4v4h-4z" /><path d="M8 7h8l1 14H7L8 7Z" /></>,
  sports: <path d="M3 10v4M6 8v8M18 8v8M21 10v4M6 12h12" />,
  books: <path d="M4 5h6a2 2 0 0 1 2 2v13a2 2 0 0 0-2-2H4V5Zm16 0h-6a2 2 0 0 0-2 2v13a2 2 0 0 1 2-2h6V5Z" />,
  supermarket: <><path d="M3 4h2l2.2 11.5h11L20 8H6" /><circle cx="9" cy="20" r="1.4" /><circle cx="17" cy="20" r="1.4" /></>,
}

export const DepartmentIcon = ({ slug, ...p }: IconProps & { slug: string }) => (
  <Icon {...p}>{departmentPaths[slug] ?? <rect x="4" y="4" width="16" height="16" rx="3" />}</Icon>
)

