import { cookies } from 'next/headers'
import type { QuestionsPage, ReviewsPage } from './review-types'

const apiUrl = process.env.API_INTERNAL_URL ?? 'http://localhost:8100'

async function get<T>(path: string): Promise<T | null> {
  try {
    const res = await fetch(`${apiUrl}/api/v1${path}`, { cache: 'no-store' })
    return res.ok ? ((await res.json()) as T) : null
  } catch {
    return null
  }
}

export const getReviews = (productId: number) => get<ReviewsPage>(`/catalog/products/${productId}/reviews`)
export const getQuestions = (productId: number) => get<QuestionsPage>(`/catalog/products/${productId}/questions`)

export async function signedIn() {
  return (await cookies()).has('rs_session')
}
