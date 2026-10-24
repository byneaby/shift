import { FormEvent, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import type { AdminClub, Me } from '../types'

export function AdminPage({ me, onLogout }: { me: Me; onLogout: () => void }) {
  const [clubs, setClubs] = useState<AdminClub[]>([])
  const [error, setError] = useState('')
  const [form, setForm] = useState({ name: '', city: '', ownerName: '', ownerEmail: '', ownerPassword: '' })

  async function load() {
    setClubs(await api<AdminClub[]>('/api/admin/clubs'))
  }

  useEffect(() => {
    load().catch((err) => setError(err instanceof Error ? err.message : 'Не загрузилось'))
  }, [])

  async function createClub(event: FormEvent) {
    event.preventDefault()
    setError('')
    try {
      await api('/api/admin/clubs', { method: 'POST', body: JSON.stringify(form) })
      setForm({ name: '', city: '', ownerName: '', ownerEmail: '', ownerPassword: '' })
      await load()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Клуб не создан')
    }
  }

  async function toggle(club: AdminClub) {
    await api(`/api/admin/clubs/${club.id}`, { method: 'PATCH', body: JSON.stringify({ active: !club.active }) })
    await load()
  }

  async function logout() {
    await api('/api/auth/logout', { method: 'POST', body: '{}' })
    onLogout()
  }

  return (
    <main className="desk">
      <header className="desk-top">
        <Link to="/" className="brand">CLUTCH</Link>
        <div>
          <strong>Платформа</strong>
          <span>{me.name}</span>
        </div>
        <button className="btn btn-ghost" type="button" onClick={logout}>Выйти</button>
      </header>
      <section className="desk-stats">
        <article><b>{clubs.length}</b><span>клубов</span></article>
        <article><b>{clubs.filter((club) => club.active).length}</b><span>работают</span></article>
        <article><b>{clubs.reduce((sum, club) => sum + club.waiting, 0)}</b><span>ждут зачисления</span></article>
      </section>
      {error && <p className="form-error">{error}</p>}
      <section className="panel">
        <h2>Новый клуб</h2>
        <form className="grid-form" onSubmit={createClub}>
          <label>Название<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required /></label>
          <label>Город<input value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} /></label>
          <label>Владелец<input value={form.ownerName} onChange={(e) => setForm({ ...form, ownerName: e.target.value })} required /></label>
          <label>Почта владельца<input type="email" value={form.ownerEmail} onChange={(e) => setForm({ ...form, ownerEmail: e.target.value })} required /></label>
          <label>Пароль владельца<input value={form.ownerPassword} onChange={(e) => setForm({ ...form, ownerPassword: e.target.value })} minLength={8} required /></label>
          <button className="btn btn-acid" type="submit">Создать</button>
        </form>
        <p className="hint">Клубу сразу открывается кейс «Кейс смены» в режиме кода. Призы владелец собирает сам.</p>
      </section>
      <section className="panel">
        <h2>Клубы</h2>
        <div className="club-list">
          {clubs.map((club) => (
            <article key={club.id}>
              <div>
                <h3>{club.name}</h3>
                <p>{club.city || 'Город не указан'} · {club.owner ? `${club.owner.name}, ${club.owner.email}` : 'владелец не найден'}</p>
                <p>{club.spins} открытий · {club.waiting} ждут</p>
              </div>
              <div className="row-actions">
                <a href={`/c/${club.slug}`} target="_blank" rel="noreferrer">Страница</a>
                <button type="button" onClick={() => toggle(club)}>{club.active ? 'Остановить' : 'Включить'}</button>
              </div>
            </article>
          ))}
        </div>
      </section>
    </main>
  )
}
