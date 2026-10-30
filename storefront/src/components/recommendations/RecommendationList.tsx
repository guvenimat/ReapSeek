'use client'

import { TrackEvent } from '@/components/analytics/Analytics'
import { ProductCard } from '@/components/ProductCard'
import { track } from '@/lib/analytics'
import type { Recommendations } from '@/lib/recommendations'

export function RecommendationList({ title, data, sourceItemId }: { title: string; data: Recommendations; sourceItemId?: number }) {
  const ids = data.items.map((i) => String(i.product.id))
  const context = { placement: data.placement, model: data.model, source_item_id: sourceItemId ? String(sourceItemId) : null, served_from: data.served_from }
  return (
    <section className="section rec-shelf" data-testid={`rec-${data.placement}`} data-served-from={data.served_from}>
      <TrackEvent event="recommendations_shown" onceKey={`${data.placement}:${sourceItemId ?? ''}:${ids.join(',')}`} properties={{ ...context, item_ids: ids, count: ids.length }} />
      <div className="section-head">
        <h2>{title}</h2>
      </div>
      <div className="shelf">
        {data.items.map((item, i) => (
          <ProductCard
            key={item.product.id}
            product={item.product}
            onSelect={() => track('recommendation_clicked', { ...context, item_id: String(item.product.id), position: i + 1, source: item.source, price: item.product.price })}
          />
        ))}
      </div>
    </section>
  )
}
