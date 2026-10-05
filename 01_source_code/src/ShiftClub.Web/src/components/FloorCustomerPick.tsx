import { useMutation } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { apiFetch } from '../api/client'
import { can, Perm } from '../permissions'

export type FloorCustomerLite = {
  id: string
  fullName: string
  phone: string
  balance: number
  bonusBalance?: number
  isBlocked?: boolean
  timeBanks?: { zoneId: string; zoneName: string; minutes: number }[]
  loyaltyTimeDiscountPercent?: number
  loyaltyLevelName?: string
}

export function FloorCustomerPick({
  query,
  onQuery,
  picked,
  onPick,
  onClear,
  hits,
  hitsLoading,
  zoneId,
  allowCreate = true,
  placeholder = 'Телефон, имя или логин',
}: {
  query: string
  onQuery: (q: string) => void
  picked: FloorCustomerLite | null
  onPick: (c: FloorCustomerLite) => void
  onClear: () => void
  hits: FloorCustomerLite[]
  hitsLoading?: boolean
  zoneId?: string | null
  allowCreate?: boolean
  placeholder?: string
}) {
  const canCreate = allowCreate && can(Perm.CustomersManage)
  const [creating, setCreating] = useState(false)
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [phone, setPhone] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)

  const bankHint = useMemo(() => {
    if (!picked) return ''
    if (!zoneId) return ''
    const m = picked.timeBanks?.find((b) => b.zoneId === zoneId)?.minutes ?? 0
    return m > 0 ? ` · банк зоны ${m} мин` : ''
  }, [picked, zoneId])

  const createMutation = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<FloorCustomerLite>('/api/customers', {
        method: 'POST',
        body: JSON.stringify({
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          phone: phone.trim(),
          email: null,
          notes: null,
          pin: pin.trim() || null,
        }),
      })
      if (!res.data) throw new Error('Не удалось создать клиента')
      return res.data
    },
    onSuccess: (c) => {
      setError(null)
      setCreating(false)
      setFirstName('')
      setLastName('')
      setPhone('')
      setPin('')
      onPick({
        ...c,
        fullName: c.fullName || `${firstName} ${lastName}`.trim(),
        balance: c.balance ?? 0,
      })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка создания'),
  })

  function openCreate() {
    const digits = query.replace(/\D/g, '')
    setPhone(digits.length >= 10 ? query.trim() : '')
    const nameGuess = digits.length >= 10 ? '' : query.trim()
    const parts = nameGuess.split(/\s+/).filter(Boolean)
    setFirstName(parts[0] ?? '')
    setLastName(parts.slice(1).join(' '))
    setPin('')
    setError(null)
    setCreating(true)
  }

  if (picked) {
    return (
      <p className="muted" style={{ margin: 0 }}>
        Выбран: <strong>{picked.fullName}</strong>
        {picked.phone ? ` · ${picked.phone}` : ''}
        {` · ${picked.balance.toLocaleString('ru-RU')} ₸`}
        {bankHint}
        {picked.loyaltyLevelName ? ` · ${picked.loyaltyLevelName}` : ''}
        <button type="button" className="ghost" style={{ marginLeft: 8 }} onClick={onClear}>
          Сбросить
        </button>
      </p>
    )
  }

  return (
    <div className="form-stack" style={{ gap: 8 }}>
      <label>
        Клиент
        <input
          value={query}
          onChange={(e) => onQuery(e.target.value)}
          placeholder={placeholder}
        />
      </label>
      {hitsLoading && query.trim().length >= 2 && <p className="muted">Ищем…</p>}
      {hits.length > 0 && (
        <ul className="zones">
          {hits.slice(0, 6).map((c) => (
            <li
              key={c.id}
              style={{ cursor: c.isBlocked ? 'not-allowed' : 'pointer', opacity: c.isBlocked ? 0.55 : 1 }}
              onClick={() => {
                if (c.isBlocked) return
                onPick(c)
              }}
            >
              <span>
                {c.fullName}
                {c.isBlocked ? ' · блок' : ''}
              </span>
              <span className="muted">
                {c.phone} · баланс {c.balance.toLocaleString('ru-RU')} ₸
                {(c.timeBanks?.length ?? 0) > 0
                  ? ` · банк ${c.timeBanks!.map((b) => `${b.zoneName}:${b.minutes}`).join(', ')}`
                  : ''}
              </span>
            </li>
          ))}
        </ul>
      )}
      {query.trim().length >= 2 && !hitsLoading && hits.length === 0 && (
        <p className="muted" style={{ margin: 0, fontSize: 12 }}>
          Не найдено. Можно создать новый аккаунт.
        </p>
      )}
      {canCreate && !creating && (
        <button type="button" className="ghost" onClick={openCreate}>
          + Новый аккаунт
        </button>
      )}
      {creating && (
        <div className="form-stack" style={{ gap: 8, padding: 10, border: '1px solid var(--border)', borderRadius: 10 }}>
          <p className="section-kicker" style={{ margin: 0 }}>
            Новый аккаунт
          </p>
          <div className="field-grid">
            <label>
              Имя
              <input value={firstName} onChange={(e) => setFirstName(e.target.value)} />
            </label>
            <label>
              Фамилия
              <input value={lastName} onChange={(e) => setLastName(e.target.value)} placeholder="необязательно" />
            </label>
          </div>
          <label>
            Телефон
            <input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="77001234567" />
          </label>
          <label>
            ПИН для ПК (необязательно)
            <input
              value={pin}
              onChange={(e) => setPin(e.target.value)}
              placeholder="4+ цифр, иначе задаст сам при первом входе"
            />
          </label>
          {error && <p className="error">{error}</p>}
          <div className="action-chip-row">
            <button
              type="button"
              disabled={createMutation.isPending || !firstName.trim() || phone.replace(/\D/g, '').length < 10}
              onClick={() => createMutation.mutate()}
            >
              {createMutation.isPending ? 'Создаём…' : 'Создать и выбрать'}
            </button>
            <button type="button" className="ghost" onClick={() => setCreating(false)} disabled={createMutation.isPending}>
              Отмена
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
