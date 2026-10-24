import { FormEvent, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { api } from '../api'
import type { Me } from '../types'

export function LoginPage({ me, onLogin }: { me: Me | null | undefined; onLogin: (me: Me) => void }) {
  const [email, setEmail] = useState('owner@nexus.local')
  const [password, setPassword] = useState('NexusOwner2026')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  if (me?.role === 'admin') return <Navigate to="/admin" replace />
  if (me?.role === 'club') return <Navigate to="/desk" replace />

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      onLogin(await api<Me>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Не получилось войти')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <form onSubmit={submit}>
        <Link to="/" className="brand">CLUTCH</Link>
        <h1>Вход в кабинет</h1>
        <p>Админ платформы, владелец клуба или сотрудник смены.</p>
        <label>
          Почта
          <input value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="username" type="email" required />
        </label>
        <label>
          Пароль
          <input value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="current-password" type="password" required />
        </label>
        {error && <p className="form-error">{error}</p>}
        <button className="btn btn-acid" type="submit" disabled={busy}>{busy ? 'Входим…' : 'Войти'}</button>
        <div className="demo-logins">
          <p>Демо</p>
          <button type="button" onClick={() => { setEmail('admin@clutch.local'); setPassword('ClutchAdmin2026') }}>Админ платформы</button>
          <button type="button" onClick={() => { setEmail('owner@nexus.local'); setPassword('NexusOwner2026') }}>Владелец клуба</button>
          <button type="button" onClick={() => { setEmail('desk@nexus.local'); setPassword('NexusDesk2026') }}>Сотрудник смены</button>
        </div>
      </form>
      <figure>
        <img src="/art/operator.jpg" alt="" />
      </figure>
    </main>
  )
}
