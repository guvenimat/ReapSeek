export function Stars({ value, size = 'small' }: { value: number; size?: 'small' | 'large' }) {
  const rounded = Math.round(value * 2) / 2
  return (
    <span className={`stars ${size}`} role="img" aria-label={`${value.toFixed(1)} out of 5 stars`}>
      {[1, 2, 3, 4, 5].map((n) => <span key={n} className={n <= rounded ? 'on' : n - 0.5 === rounded ? 'half' : ''} aria-hidden="true">★</span>)}
    </span>
  )
}
