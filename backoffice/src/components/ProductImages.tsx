import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import type { ImageApi, ProductImageRow, Variant } from '../api'
import { Badge, Card, FormError } from './ui'
import { useAction } from '../lib'

const accepted = 'image/jpeg,image/png,image/webp'

export function ProductImages({ images: client, variants, sellerMode, onChanged }: {
  images: ImageApi; variants: Variant[]; sellerMode?: boolean; onChanged?: () => void
}) {
  const [images, setImages] = useState<ProductImageRow[]>()
  const [hidden, setHidden] = useState(false)
  const [color, setColor] = useState('')
  const action = useAction()
  const input = useRef<HTMLInputElement>(null)
  const colors = [...new Set(variants.map((v) => v.options.Color ?? v.options.color).filter((c): c is string => !!c))]

  useEffect(() => {
    client.list().then(setImages, () => setHidden(true))
  }, [client])

  if (hidden) return null
  const apply = async (work: () => Promise<ProductImageRow[]>) => {
    const next = await action.run(work)
    if (next) {
      setImages(next)
      onChanged?.()
    }
  }
  const pick = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = ''
    if (file) await apply(() => client.upload(file, color || undefined))
  }
  const uploads = images?.filter((i) => i.uploaded).length ?? 0

  return (
    <Card title="Images" testId="product-images" actions={
      <div className="row">
        {colors.length > 0 && (
          <select value={color} onChange={(e) => setColor(e.target.value)} aria-label="Color of the next image">
            <option value="">Any color</option>
            {colors.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        )}
        <input ref={input} type="file" accept={accepted} hidden onChange={pick} data-testid="image-file" />
        <button className="btn btn-primary" disabled={action.busy || uploads >= 8} onClick={() => input.current?.click()}>
          {action.busy ? 'Uploading...' : 'Upload image'}
        </button>
      </div>
    }>
      <p className="muted small">
        JPEG, PNG or WebP, up to 5 MB, between 200 and 8000 pixels per side; up to 8 uploaded images. Uploaded photos come before the generated illustrations,
        and the first image is the one shown on cards and in search.{sellerMode ? ' You can change the images until the proposal is approved.' : ''}
      </p>
      <FormError error={action.error} />
      <div className="image-grid">
        {images?.map((i) => (
          <figure key={i.id} className="image-tile" data-image={i.id}>
            <img src={i.url} alt={i.alt} />
            <figcaption>
              <div className="row">
                {i.position === 0 && <Badge tone="brand">Main</Badge>}
                {i.uploaded ? <Badge tone="good">Photo</Badge> : <Badge>Illustration</Badge>}
                {i.color && <span className="muted small">{i.color}</span>}
              </div>
              <div className="row">
                {i.position !== 0 && <button className="link small" disabled={action.busy} onClick={() => apply(() => client.makeFirst(i.id))}>Make main</button>}
                {(i.uploaded || !sellerMode) && images.length > 1 && (
                  <button className="link small danger" disabled={action.busy} onClick={() => apply(() => client.remove(i.id))}>Remove</button>
                )}
              </div>
            </figcaption>
          </figure>
        ))}
      </div>
    </Card>
  )
}
