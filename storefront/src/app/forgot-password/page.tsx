import type { Metadata } from 'next'
import { ForgotPasswordForm } from '@/components/AccountRecovery'

export const metadata: Metadata = { title: 'Forgot your password', robots: { index: false } }

export default function ForgotPasswordPage() {
  return (
    <div className="container auth-page">
      <h1>Forgot your password?</h1>
      <p className="muted">Enter the email address of your account and we will send you a link to choose a new password.</p>
      <ForgotPasswordForm />
    </div>
  )
}
