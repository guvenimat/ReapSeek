import { useState, type FormEvent } from 'react'
import { api, sellerApi, type SellerQuestion } from '../api'
import { Badge, Card, FormError, Loading } from '../components/ui'
import { formatDate, navigate, plural, useAction, useLoad } from '../lib'

export function Moderation({ query }: { query: URLSearchParams }) {
  const status = query.get('status') ?? ''
  const data = useLoad(() => api.moderation(status || undefined), [status])
  const action = useAction()
  const set = async (kind: 'reviews' | 'questions', id: number, next: 'visible' | 'hidden') => {
    if ((await action.run(() => api.moderate(kind, id, next))) !== undefined) data.reload()
  }
  const filter = (
    <select value={status} onChange={(e) => navigate('moderation', { status: e.target.value })} aria-label="Status">
      <option value="">All</option>
      <option value="visible">Visible</option>
      <option value="hidden">Hidden</option>
    </select>
  )
  return (
    <Loading state={data}>
      {(d) => (
        <div className="stack">
          <FormError error={action.error} />
          <Card title={plural(d.reviews.length, 'review')} actions={filter}>
            <table className="table" data-testid="moderation-reviews">
              <thead><tr><th>Product</th><th>Review</th><th>Author</th><th>Status</th><th /></tr></thead>
              <tbody>
                {d.reviews.map((r) => (
                  <tr key={r.id} data-review={r.id}>
                    <td className="strong">{r.product_title}</td>
                    <td><div><span className="stars-text">{'★'.repeat(r.rating)}{'☆'.repeat(5 - r.rating)}</span> <strong>{r.title}</strong></div><div className="muted small">{r.body}</div></td>
                    <td className="muted">{r.author}<div className="small">{formatDate(r.created_at)}</div></td>
                    <td><Badge tone={r.status === 'visible' ? 'good' : 'bad'}>{r.status}</Badge></td>
                    <td className="actions">
                      <button className="btn btn-ghost btn-sm" disabled={action.busy} onClick={() => set('reviews', r.id, r.status === 'visible' ? 'hidden' : 'visible')}>
                        {r.status === 'visible' ? 'Hide' : 'Show'}
                      </button>
                    </td>
                  </tr>
                ))}
                {d.reviews.length === 0 && <tr><td colSpan={5} className="empty">No reviews.</td></tr>}
              </tbody>
            </table>
          </Card>
          <Card title={plural(d.questions.length, 'question')}>
            <table className="table" data-testid="moderation-questions">
              <thead><tr><th>Product</th><th>Question</th><th>Author</th><th className="num">Answers</th><th>Status</th><th /></tr></thead>
              <tbody>
                {d.questions.map((q) => (
                  <tr key={q.id} data-question={q.id}>
                    <td className="strong">{q.product_title}</td>
                    <td>{q.body}</td>
                    <td className="muted">{q.author}<div className="small">{formatDate(q.created_at)}</div></td>
                    <td className="num">{q.answer_count}</td>
                    <td><Badge tone={q.status === 'visible' ? 'good' : 'bad'}>{q.status}</Badge></td>
                    <td className="actions">
                      <button className="btn btn-ghost btn-sm" disabled={action.busy} onClick={() => set('questions', q.id, q.status === 'visible' ? 'hidden' : 'visible')}>
                        {q.status === 'visible' ? 'Hide' : 'Show'}
                      </button>
                    </td>
                  </tr>
                ))}
                {d.questions.length === 0 && <tr><td colSpan={6} className="empty">No questions.</td></tr>}
              </tbody>
            </table>
          </Card>
        </div>
      )}
    </Loading>
  )
}

export function SellerQuestions({ query }: { query: URLSearchParams }) {
  const filter = query.get('filter') ?? 'unanswered'
  const questions = useLoad(() => sellerApi.questions(filter === 'all' ? undefined : filter === 'answered'), [filter])
  return (
    <Card
      title={questions.data ? plural(questions.data.length, 'question') : 'Questions'}
      actions={
        <select value={filter} onChange={(e) => navigate('questions', { filter: e.target.value })} aria-label="Filter">
          <option value="unanswered">To answer</option>
          <option value="answered">Answered</option>
          <option value="all">All</option>
        </select>
      }
    >
      <p className="muted small">Shoppers ask about products you sell. Your answer appears on the product page under your store name, and the shopper gets an email.</p>
      <Loading state={questions}>
        {(list) => (
          <div className="stack" data-testid="seller-questions">
            {list.map((q) => <QuestionCard key={q.id} question={q} onAnswered={questions.reload} />)}
            {list.length === 0 && <p className="empty">Nothing here.</p>}
          </div>
        )}
      </Loading>
    </Card>
  )
}

function QuestionCard({ question, onAnswered }: { question: SellerQuestion; onAnswered: () => void }) {
  const action = useAction()
  const [body, setBody] = useState(question.my_answer ?? '')
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (await action.run(() => sellerApi.answer(question.id, body))) onAnswered()
  }
  return (
    <form className="package" data-question={question.id} onSubmit={submit}>
      <div className="package-head">
        <div>
          <strong>{question.product_title}</strong>
          <div className="muted small">{question.author} · {formatDate(question.created_at)} · {plural(question.answer_count, 'answer')}</div>
        </div>
        {question.my_answer && <Badge tone="good">Answered</Badge>}
      </div>
      <p className="strong">“{question.body}”</p>
      <div className="row wrap">
        <input className="grow" required minLength={2} maxLength={1000} placeholder="Your answer" value={body} onChange={(e) => setBody(e.target.value)} aria-label="Your answer" />
        <button type="submit" className="btn btn-primary btn-sm" disabled={action.busy || body.trim().length < 2}>{question.my_answer ? 'Update answer' : 'Answer'}</button>
      </div>
      <FormError error={action.error} />
    </form>
  )
}
