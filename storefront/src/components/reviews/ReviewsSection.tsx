import { getQuestions, getReviews, signedIn } from '@/lib/reviews'
import { AskQuestion } from './AskQuestion'
import { ReviewList } from './ReviewList'
import { Stars } from './Stars'
import { WriteReview } from './WriteReview'

const date = (value: string) => new Date(value).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Europe/Istanbul' })

export async function ReviewsSection({ productId, slug }: { productId: number; slug: string }) {
  const [reviews, questions, isSignedIn] = await Promise.all([getReviews(productId), getQuestions(productId), signedIn()])
  const summary = reviews?.summary
  return (
    <>
      <section className="pdp-reviews" id="reviews" data-testid="reviews">
        <div className="reviews-summary">
          <h2>Customer reviews</h2>
          {summary && summary.count > 0 ? (
            <>
              <div className="reviews-average" data-testid="reviews-average"><strong>{summary.average?.toFixed(1)}</strong><Stars value={summary.average ?? 0} size="large" /></div>
              <p className="muted small">{summary.count} review{summary.count === 1 ? '' : 's'} from verified buyers</p>
              <ol className="reviews-bars">
                {summary.distribution.map((n, i) => (
                  <li key={i}><span>{5 - i} ★</span><span className="bar"><span style={{ width: `${(n / summary.count) * 100}%` }} /></span><span className="muted">{n}</span></li>
                ))}
              </ol>
            </>
          ) : <p className="muted">No reviews yet. Reviews come only from customers who received the product.</p>}
          <WriteReview productId={productId} signedIn={isSignedIn} slug={slug} />
        </div>
        <ReviewList productId={productId} initial={reviews} />
      </section>
      <section className="pdp-questions" id="questions" data-testid="questions">
        <h2>Questions and answers</h2>
        <AskQuestion productId={productId} signedIn={isSignedIn} slug={slug} />
        {questions && questions.questions.length > 0 ? (
          <ul className="question-list">
            {questions.questions.map((q) => (
              <li key={q.id} data-question={q.id}>
                <p className="question"><strong>Q:</strong> {q.body}</p>
                <p className="muted small">{q.author} · {date(q.created_at)}</p>
                {q.answers.length === 0 && <p className="muted small">The sellers have not answered yet.</p>}
                {q.answers.map((a) => (
                  <div key={a.id} className="answer"><strong>{a.seller_name}</strong> <span className="muted small">seller · {date(a.created_at)}</span><p>{a.body}</p></div>
                ))}
              </li>
            ))}
          </ul>
        ) : <p className="muted">No questions yet. Ask the sellers anything about this product.</p>}
      </section>
    </>
  )
}
