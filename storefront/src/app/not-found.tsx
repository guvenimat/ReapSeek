import Link from 'next/link'

export default function NotFound() {
  return (
    <div className="container">
      <div className="empty-state">
        <strong>We couldn&apos;t find that page</strong>
        <span>It may have moved, or the link may be wrong.</span>
        <Link href="/" className="btn btn-primary">Back to the home page</Link>
      </div>
    </div>
  )
}
