import { Suspense } from 'react'
import { getRecommendations, type Placement } from '@/lib/recommendations'
import { RecommendationList } from './RecommendationList'

type Props = { placement: Placement; title: string; productId?: number; limit?: number }

const minimumItems = 2

export function RecommendationShelf(props: Props) {
  return (
    <Suspense fallback={<ShelfSkeleton title={props.title} />}>
      <Shelf {...props} />
    </Suspense>
  )
}

async function Shelf({ placement, title, productId, limit = 10 }: Props) {
  const data = await getRecommendations(placement, { productId, limit })
  if (!data || data.items.length < minimumItems) return null
  return <RecommendationList title={title} data={data} sourceItemId={productId} />
}

function ShelfSkeleton({ title }: { title: string }) {
  return (
    <section className="section rec-shelf" aria-busy="true">
      <div className="section-head"><h2>{title}</h2></div>
      <div className="shelf">
        {Array.from({ length: 5 }, (_, i) => <div key={i} className="product-card skeleton" />)}
      </div>
    </section>
  )
}
