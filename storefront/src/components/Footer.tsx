import Link from 'next/link'
import { LogoMark } from './icons'

export function Footer() {
  return (
    <footer className="site-footer">
      <div className="container footer-inner">
        <div className="footer-brand">
          <Link href="/" className="logo" aria-label="ReapSeek home">
            <LogoMark size={28} />
            <span className="logo-text">Reap<strong>Seek</strong></span>
          </Link>
          <p>A multi-seller marketplace. Analytics and recommendations by ReapDeep.</p>
        </div>
        <p className="footer-note">
          © 2026 ReapSeek. A portfolio project, no real orders or payments.{' '}
          <a href="https://github.com/guvenimat/ReapSeek" rel="noreferrer">Source on GitHub</a>
        </p>
      </div>
    </footer>
  )
}
