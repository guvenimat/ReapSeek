import { useEffect, useState } from 'react'
import { api, ApiError, type User } from './api'
import { Login } from './pages/Login'
import { Shell } from './pages/Shell'

type State = { status: 'loading' } | { status: 'signed-out' } | { status: 'signed-in'; user: User }

export function App() {
  const [state, setState] = useState<State>({ status: 'loading' })

  useEffect(() => {
    api.me()
      .then((user) => setState({ status: 'signed-in', user }))
      .catch((err) => {
        if (!(err instanceof ApiError)) console.error(err)
        setState({ status: 'signed-out' })
      })
  }, [])

  if (state.status === 'loading') return <div className="boot" aria-busy="true" />
  if (state.status === 'signed-out') return <Login onSignedIn={(user) => setState({ status: 'signed-in', user })} />
  return <Shell user={state.user} onSignedOut={() => setState({ status: 'signed-out' })} />
}
