import { useState, type FormEvent } from 'react'
import { api } from '../api'
import { Card, FormError, Loading } from '../components/ui'
import { formatCount, navigate, useAction, useLoad } from '../lib'

const kinds = ['bag', 'book', 'bottle', 'box', 'dress', 'dumbbell', 'headphones', 'jar', 'lamp', 'laptop', 'mug', 'pants', 'phone', 'shirt', 'shoe', 'sofa', 'toy', 'watch']

export function Categories() {
  const categories = useLoad(() => api.categories(), [])
  const action = useAction()
  const [form, setForm] = useState({ parent_id: 0, name: '', image_kind: '' })

  const create = async (e: FormEvent) => {
    e.preventDefault()
    const created = await action.run(() => api.createCategory({ parent_id: form.parent_id || null, name: form.name, image_kind: form.image_kind || undefined }))
    if (created) {
      setForm({ ...form, name: '' })
      categories.reload()
    }
  }

  return (
    <Card title="Category tree">
      <Loading state={categories}>
        {(list) => (
          <div className="tree" data-testid="category-tree">
            {list.filter((c) => c.depth === 0).map((d) => (
              <div key={d.id} className="tree-group">
                <button type="button" className="tree-node department" onClick={() => navigate('products', { category: d.path })}>
                  <strong>{d.name}</strong>
                  <span className="muted small">{formatCount(d.product_count)} products</span>
                </button>
                <div className="tree-children">
                  {list.filter((c) => c.parent_id === d.id).map((c) => (
                    <button key={c.id} type="button" className="tree-node" onClick={() => navigate('products', { category: c.path })}>
                      <span>{c.name}</span>
                      <span className="muted small">{formatCount(c.product_count)} · {c.image_kind}</span>
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}
      </Loading>
      <form className="inline-form" onSubmit={create} data-testid="add-category">
        <div className="form-title">Add a category</div>
        <div className="row wrap">
          <select value={form.parent_id} onChange={(e) => setForm({ ...form, parent_id: Number(e.target.value) })} aria-label="Parent">
            <option value={0}>New department</option>
            {categories.data?.filter((c) => c.depth === 0).map((c) => <option key={c.id} value={c.id}>Under {c.name}</option>)}
          </select>
          <input required maxLength={80} placeholder="Name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} aria-label="Category name" />
          <select value={form.image_kind} onChange={(e) => setForm({ ...form, image_kind: e.target.value })} aria-label="Image style">
            <option value="">Image style: inherit</option>
            {kinds.map((k) => <option key={k} value={k}>{k}</option>)}
          </select>
          <button type="submit" className="btn btn-primary" disabled={action.busy}>Add category</button>
        </div>
        <FormError error={action.error} />
      </form>
    </Card>
  )
}
