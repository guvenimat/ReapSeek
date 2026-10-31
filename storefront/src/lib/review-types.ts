export type Review = { id: number; rating: number; title: string; body: string; author: string; variant_label: string | null; verified: boolean; created_at: string }
export type ReviewsPage = {
  summary: { average: number | null; count: number; distribution: number[] }
  reviews: Review[]; total: number; page: number; page_size: number
}
export type Answer = { id: number; seller_name: string; body: string; created_at: string }
export type Question = { id: number; body: string; author: string; created_at: string; answers: Answer[] }
export type QuestionsPage = { questions: Question[]; total: number; page: number; page_size: number }
