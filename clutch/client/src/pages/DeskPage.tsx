import { FormEvent, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { rarityLabel, statusLabel, typeLabel, when } from '../labels'
import type { ClubCase, ClubDesk, DeskSpin, Me, PrizeType, Rarity, StaffPrize, StaffUser, Ticket } from '../types'

const art = ['/art/prize-bonus.jpg', '/art/prize-time.jpg', '/art/prize-drink.jpg', '/art/prize-headset.jpg', '/art/rune-stone.jpg']

export function DeskPage({ me, onLogout }: { me: Me; onLogout: () => void }) {
  const [tab, setTab] = useState<'floor' | 'cases' | 'team'>('floor')
  const [club, setClub] = useState<ClubDesk | null>(null)
  const [spins, setSpins] = useState<DeskSpin[]>([])
  const [cases, setCases] = useState<ClubCase[]>([])
  const [filter, setFilter] = useState<'open' | 'all'>('open')
  const [error, setError] = useState('')

  async function reload() {
    const [nextClub, nextSpins, nextCases] = await Promise.all([
      api<ClubDesk>('/api/club'),
      api<DeskSpin[]>(`/api/club/spins${filter === 'open' ? '?status=open' : ''}`),
      api<ClubCase[]>('/api/club/cases'),
    ])
    setClub(nextClub)
    setSpins(nextSpins)
    setCases(nextCases)
  }

  useEffect(() => {
    reload().catch((err) => setError(err instanceof Error ? err.message : 'Кабинет не открылся'))
  }, [filter])

  useEffect(() => {
    const id = window.setInterval(() => {
      api<DeskSpin[]>(`/api/club/spins${filter === 'open' ? '?status=open' : ''}`).then(setSpins).catch(() => undefined)
      api<ClubDesk>('/api/club').then(setClub).catch(() => undefined)
    }, 4000)
    return () => window.clearInterval(id)
  }, [filter])

  async function act(id: string, kind: 'credit' | 'void') {
    setError('')
    try {
      await api(`/api/club/spins/${id}/${kind}`, { method: 'POST', body: '{}' })
      await reload()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Не сохранилось')
    }
  }

  async function logout() {
    await api('/api/auth/logout', { method: 'POST', body: '{}' })
    onLogout()
  }

  const link = club && cases[0] ? `${location.origin}/c/${club.slug}/${cases[0].slug}` : ''

  return (
    <main className="desk">
      <header className="desk-top">
        <Link to="/" className="brand">CLUTCH</Link>
        <div>
          <strong>{club?.name || me.club?.name}</strong>
          <span>{me.clubRole === 'owner' ? 'Владелец' : 'Смена'} · {me.name}</span>
        </div>
        <button className="btn btn-ghost" type="button" onClick={logout}>Выйти</button>
      </header>
      <nav className="tabs">
        <button type="button" className={tab === 'floor' ? 'is-on' : ''} onClick={() => setTab('floor')}>Смена</button>
        {me.clubRole === 'owner' && <button type="button" className={tab === 'cases' ? 'is-on' : ''} onClick={() => setTab('cases')}>Кейсы</button>}
        {me.clubRole === 'owner' && <button type="button" className={tab === 'team' ? 'is-on' : ''} onClick={() => setTab('team')}>Клуб</button>}
      </nav>
      {error && <p className="form-error">{error}</p>}
      {club && (
        <section className="desk-stats">
          <article><b>{club.today}</b><span>открытий сегодня</span></article>
          <article><b>{club.waiting}</b><span>ждут зачисления</span></article>
          <article><b>{club.creditedToday}</b><span>зачислено сегодня</span></article>
        </section>
      )}
      {tab === 'floor' && (
        <section className="panel">
          {link && (
            <div className="link-box">
              <div>
                <p>Страница кейса</p>
                <a href={link}>{link.replace(/^https?:\/\//, '')}</a>
              </div>
              <button type="button" className="btn btn-ghost" onClick={() => navigator.clipboard.writeText(link)}>Копировать</button>
            </div>
          )}
          <div className="filter">
            <button type="button" className={filter === 'open' ? 'is-on' : ''} onClick={() => setFilter('open')}>Ждут</button>
            <button type="button" className={filter === 'all' ? 'is-on' : ''} onClick={() => setFilter('all')}>Все</button>
          </div>
          {cases.filter((item) => item.openMode === 'ticket').map((item) => (
            <TicketBox key={item.id} item={item} />
          ))}
          {spins.length === 0 && <p className="hint">Пока пусто. Открытия с страницы кейса появятся здесь сами.</p>}
          <div className="spin-list">
            {spins.map((spin) => (
              <article key={spin.id} className={spin.status === 'open' ? 'is-live' : ''}>
                {spin.image && <img src={spin.image} alt="" />}
                <div>
                  <p className="code">{spin.code}</p>
                  <h3>{spin.title}</h3>
                  <p>{spin.detail} · {rarityLabel[spin.rarity]} · {when(spin.createdAt)}</p>
                  <p className="hint">{spin.cashierHint}</p>
                </div>
                <div className="row-actions">
                  {spin.status === 'open' ? (
                    <>
                      <button type="button" className="btn btn-acid" onClick={() => act(spin.id, 'credit')}>Зачислил</button>
                      <button type="button" onClick={() => act(spin.id, 'void')}>Скрыть</button>
                    </>
                  ) : (
                    <span>{statusLabel[spin.status]}</span>
                  )}
                </div>
              </article>
            ))}
          </div>
        </section>
      )}
      {tab === 'cases' && me.clubRole === 'owner' && (
        <CasesEditor cases={cases} onChange={reload} onError={setError} />
      )}
      {tab === 'team' && me.clubRole === 'owner' && club && (
        <TeamEditor club={club} onChange={reload} onError={setError} />
      )}
    </main>
  )
}

function TicketBox({ item }: { item: ClubCase }) {
  const [tickets, setTickets] = useState<Ticket[]>([])
  const [fresh, setFresh] = useState<string[]>([])

  async function load() {
    setTickets(await api<Ticket[]>(`/api/club/cases/${item.id}/tickets`))
  }
  useEffect(() => { load().catch(() => undefined) }, [item.id])

  async function issue() {
    const data = await api<{ codes: string[] }>(`/api/club/cases/${item.id}/tickets`, { method: 'POST', body: JSON.stringify({ count: 1 }) })
    setFresh(data.codes)
    await load()
  }

  return (
    <div className="ticket-box">
      <div>
        <h3>{item.title}: код открытия</h3>
        <p className="hint">Гость вводит код на странице. Код сгорает после одного открытия.</p>
        {fresh[0] && <p className="result-code">{fresh[0]}</p>}
      </div>
      <button type="button" className="btn btn-ghost" onClick={issue}>Выдать код</button>
      <ul>
        {tickets.filter((ticket) => !ticket.usedAt).slice(0, 6).map((ticket) => <li key={ticket.code}>{ticket.code}</li>)}
      </ul>
    </div>
  )
}

function CasesEditor({ cases, onChange, onError }: { cases: ClubCase[]; onChange: () => Promise<void>; onError: (msg: string) => void }) {
  const [title, setTitle] = useState('')

  async function addCase(event: FormEvent) {
    event.preventDefault()
    try {
      await api('/api/club/cases', { method: 'POST', body: JSON.stringify({ title }) })
      setTitle('')
      await onChange()
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Кейс не создан')
    }
  }

  return (
    <section className="panel">
      {cases.map((item) => <CaseCard key={item.id} item={item} onChange={onChange} onError={onError} />)}
      <form className="inline-form" onSubmit={addCase}>
        <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Новый кейс, например Ночной" />
        <button className="btn btn-ghost" type="submit">Добавить кейс</button>
      </form>
    </section>
  )
}

function CaseCard({ item, onChange, onError }: { item: ClubCase; onChange: () => Promise<void>; onError: (msg: string) => void }) {
  const [draft, setDraft] = useState(item)
  useEffect(() => setDraft(item), [item])

  async function saveCase(event: FormEvent) {
    event.preventDefault()
    try {
      await api(`/api/club/cases/${item.id}`, {
        method: 'PATCH',
        body: JSON.stringify({
          title: draft.title,
          lead: draft.lead,
          openMode: draft.openMode,
          cooldownSeconds: Number(draft.cooldownSeconds),
          showOdds: draft.showOdds,
          enabled: draft.enabled,
        }),
      })
      await onChange()
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Кейс не сохранился')
    }
  }

  return (
    <article className="case-card">
      <form className="grid-form" onSubmit={saveCase}>
        <label>Название<input value={draft.title} onChange={(e) => setDraft({ ...draft, title: e.target.value })} /></label>
        <label>Текст для гостя<input value={draft.lead} onChange={(e) => setDraft({ ...draft, lead: e.target.value })} /></label>
        <label>Кто крутит
          <select value={draft.openMode} onChange={(e) => setDraft({ ...draft, openMode: e.target.value as ClubCase['openMode'] })}>
            <option value="public">Любой по ссылке</option>
            <option value="ticket">Только с кодом смены</option>
          </select>
        </label>
        <label>Пауза, секунды<input type="number" min={0} max={3600} value={draft.cooldownSeconds} onChange={(e) => setDraft({ ...draft, cooldownSeconds: Number(e.target.value) })} /></label>
        <label className="check"><input type="checkbox" checked={draft.showOdds} onChange={(e) => setDraft({ ...draft, showOdds: e.target.checked })} /> Показывать шансы гостю</label>
        <label className="check"><input type="checkbox" checked={draft.enabled} onChange={(e) => setDraft({ ...draft, enabled: e.target.checked })} /> Кейс включён</label>
        <button className="btn btn-acid" type="submit">Сохранить кейс</button>
      </form>
      <p className="hint">Адрес: /c/…/{item.slug}</p>
      <div className="prize-edit">
        {item.prizes.map((prize) => (
          <PrizeEditor key={prize.id} prize={prize} onChange={onChange} onError={onError} />
        ))}
        <PrizeEditor caseId={item.id} onChange={onChange} onError={onError} />
      </div>
    </article>
  )
}

function PrizeEditor({ prize, caseId, onChange, onError }: { prize?: StaffPrize; caseId?: string; onChange: () => Promise<void>; onError: (msg: string) => void }) {
  const blank = {
    title: '',
    detail: '',
    rarity: 'common' as Rarity,
    prizeType: 'bonus' as PrizeType,
    weight: 10,
    cashierHint: '',
    image: art[0],
    dailyLimit: '',
    totalLimit: '',
    active: true,
  }
  const [form, setForm] = useState(prize ? {
    title: prize.title,
    detail: prize.detail,
    rarity: prize.rarity,
    prizeType: prize.prizeType,
    weight: prize.weight,
    cashierHint: prize.cashierHint,
    image: prize.image || '',
    dailyLimit: prize.dailyLimit ?? '',
    totalLimit: prize.totalLimit ?? '',
    active: prize.active,
  } : blank)

  async function save(event: FormEvent) {
    event.preventDefault()
    const body = {
      ...form,
      dailyLimit: form.dailyLimit === '' ? null : Number(form.dailyLimit),
      totalLimit: form.totalLimit === '' ? null : Number(form.totalLimit),
    }
    try {
      if (prize) await api(`/api/club/prizes/${prize.id}`, { method: 'PATCH', body: JSON.stringify(body) })
      else {
        await api(`/api/club/cases/${caseId}/prizes`, { method: 'POST', body: JSON.stringify(body) })
        setForm(blank)
      }
      await onChange()
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Приз не сохранился')
    }
  }

  async function remove() {
    if (!prize) return
    await api(`/api/club/prizes/${prize.id}`, { method: 'DELETE' })
    await onChange()
  }

  async function upload(file: File) {
    const dataUrl = await fileToDataUrl(file)
    const data = await api<{ url: string }>('/api/club/upload', { method: 'POST', body: JSON.stringify({ dataUrl }) })
    setForm((prev) => ({ ...prev, image: data.url }))
  }

  return (
    <form className="prize-form" onSubmit={save}>
      <div className="prize-form-top">
        {form.image && <img src={form.image} alt="" />}
        <strong>{prize ? form.title || 'Приз' : 'Новый приз'}</strong>
        {prize && <button type="button" onClick={remove}>Удалить</button>}
      </div>
      <label>Название<input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} required /></label>
      <label>Что видит гость<input value={form.detail} onChange={(e) => setForm({ ...form, detail: e.target.value })} /></label>
      <label>Что ввести в кассе<textarea value={form.cashierHint} onChange={(e) => setForm({ ...form, cashierHint: e.target.value })} required /></label>
      <div className="split-fields">
        <label>Тип
          <select value={form.prizeType} onChange={(e) => setForm({ ...form, prizeType: e.target.value as PrizeType })}>
            {(['bonus', 'time', 'bar', 'custom'] as PrizeType[]).map((type) => <option key={type} value={type}>{typeLabel[type]}</option>)}
          </select>
        </label>
        <label>Редкость
          <select value={form.rarity} onChange={(e) => setForm({ ...form, rarity: e.target.value as Rarity })}>
            {(['common', 'uncommon', 'rare', 'epic', 'legendary'] as Rarity[]).map((rarity) => <option key={rarity} value={rarity}>{rarityLabel[rarity]}</option>)}
          </select>
        </label>
        <label>Вес<input type="number" min={1} value={form.weight} onChange={(e) => setForm({ ...form, weight: Number(e.target.value) })} /></label>
        <label>Лимит в день<input type="number" min={0} value={form.dailyLimit} placeholder="нет" onChange={(e) => setForm({ ...form, dailyLimit: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
        <label>Лимит всего<input type="number" min={0} value={form.totalLimit} placeholder="нет" onChange={(e) => setForm({ ...form, totalLimit: e.target.value === '' ? '' : Number(e.target.value) })} /></label>
      </div>
      <div className="art-pick">
        {art.map((src) => (
          <button type="button" key={src} className={form.image === src ? 'is-on' : ''} onClick={() => setForm({ ...form, image: src })}>
            <img src={src} alt="" />
          </button>
        ))}
        <label className="file">Своя картинка
          <input type="file" accept="image/png,image/jpeg,image/webp" onChange={(e) => { const file = e.target.files?.[0]; if (file) upload(file).catch((err) => onError(err instanceof Error ? err.message : 'Картинка не встала')) }} />
        </label>
      </div>
      <label className="check"><input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} /> Приз участвует</label>
      <button className="btn btn-ghost" type="submit">{prize ? 'Сохранить приз' : 'Добавить приз'}</button>
    </form>
  )
}

function TeamEditor({ club, onChange, onError }: { club: ClubDesk; onChange: () => Promise<void>; onError: (msg: string) => void }) {
  const [staff, setStaff] = useState<StaffUser[]>([])
  const [profile, setProfile] = useState({ name: club.name, city: club.city, tagline: club.tagline, accent: club.accent })
  const [person, setPerson] = useState({ name: '', email: '', password: '', clubRole: 'staff' })

  useEffect(() => {
    api<StaffUser[]>('/api/club/staff').then(setStaff).catch((err) => onError(err instanceof Error ? err.message : 'Команда не загрузилась'))
  }, [club.id, onError])

  async function saveClub(event: FormEvent) {
    event.preventDefault()
    try {
      await api('/api/club', { method: 'PATCH', body: JSON.stringify(profile) })
      await onChange()
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Клуб не сохранился')
    }
  }

  async function addPerson(event: FormEvent) {
    event.preventDefault()
    try {
      await api('/api/club/staff', { method: 'POST', body: JSON.stringify(person) })
      setPerson({ name: '', email: '', password: '', clubRole: 'staff' })
      setStaff(await api<StaffUser[]>('/api/club/staff'))
    } catch (err) {
      onError(err instanceof Error ? err.message : 'Сотрудник не добавлен')
    }
  }

  async function toggle(member: StaffUser) {
    await api(`/api/club/staff/${member.id}`, { method: 'PATCH', body: JSON.stringify({ active: !member.active }) })
    setStaff(await api<StaffUser[]>('/api/club/staff'))
  }

  return (
    <section className="panel">
      <h2>Клуб на странице</h2>
      <form className="grid-form" onSubmit={saveClub}>
        <label>Название<input value={profile.name} onChange={(e) => setProfile({ ...profile, name: e.target.value })} /></label>
        <label>Город<input value={profile.city} onChange={(e) => setProfile({ ...profile, city: e.target.value })} /></label>
        <label>Короткая фраза<input value={profile.tagline} onChange={(e) => setProfile({ ...profile, tagline: e.target.value })} /></label>
        <label>Цвет<input value={profile.accent} onChange={(e) => setProfile({ ...profile, accent: e.target.value })} /></label>
        <button className="btn btn-acid" type="submit">Сохранить клуб</button>
      </form>
      <h2>Люди</h2>
      <div className="club-list">
        {staff.map((member) => (
          <article key={member.id}>
            <div>
              <h3>{member.name}</h3>
              <p>{member.email} · {member.clubRole === 'owner' ? 'владелец' : 'смена'}</p>
            </div>
            <button type="button" onClick={() => toggle(member)}>{member.active ? 'Отключить' : 'Включить'}</button>
          </article>
        ))}
      </div>
      <form className="grid-form" onSubmit={addPerson}>
        <label>Имя<input value={person.name} onChange={(e) => setPerson({ ...person, name: e.target.value })} required /></label>
        <label>Почта<input type="email" value={person.email} onChange={(e) => setPerson({ ...person, email: e.target.value })} required /></label>
        <label>Пароль<input value={person.password} onChange={(e) => setPerson({ ...person, password: e.target.value })} minLength={8} required /></label>
        <label>Роль
          <select value={person.clubRole} onChange={(e) => setPerson({ ...person, clubRole: e.target.value })}>
            <option value="staff">Сотрудник смены</option>
            <option value="owner">Владелец</option>
          </select>
        </label>
        <button className="btn btn-ghost" type="submit">Добавить</button>
      </form>
    </section>
  )
}

function fileToDataUrl(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(new Error('Файл не прочитался'))
    reader.readAsDataURL(file)
  })
}
