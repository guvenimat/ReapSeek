import { shipmentLabel, shipmentSteps, type TrackingEvent } from '@/lib/order-types'

const time = (value: string) => new Date(value).toLocaleString('en-GB', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Istanbul' })

export function Tracking({ number, current, events, startedAt }: { number: string; current: string | null; events: TrackingEvent[]; startedAt: string | null }) {
  const reached = shipmentSteps.indexOf((current ?? 'label_created') as (typeof shipmentSteps)[number])
  const at = (step: string) => step === 'label_created' ? startedAt : events.find((e) => e.status === step)?.occurred_at ?? null
  return (
    <div className="tracking" data-testid="tracking">
      <div className="muted small">Sandbox Cargo · <strong>{number}</strong></div>
      <ol className="tracking-steps">
        {shipmentSteps.map((step, i) => {
          const when = at(step)
          return (
            <li key={step} className={i < reached ? 'done' : i === reached ? 'current' : ''} data-step={step}>
              <span className="dot" aria-hidden="true" />
              <span>{shipmentLabel[step]}</span>
              {i <= reached && when && <span className="muted small">{time(when)}</span>}
            </li>
          )
        })}
      </ol>
    </div>
  )
}
