import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiFetch } from '../api/client'
import type { LicenseStatusDto } from '../api/client'

const FEATURE_LABELS: Record<string, string> = {
  shift_case: 'Кейсы (рулетка)',
  multi_branch: 'Несколько филиалов',
  telegram_crm: 'Telegram CRM',
  kaspi_pos: 'Kaspi Smart POS',
}

const STATE_LABELS: Record<string, string> = {
  Missing: 'Не установлена',
  Active: 'Действует',
  Grace: 'Льготный период',
  Expired: 'Истекла',
  Invalid: 'Недействительна',
}

function stateTone(state: string): string {
  if (state === 'Active') return 'ok'
  if (state === 'Missing') return 'muted'
  return 'error'
}

export function LicensePage() {
  const queryClient = useQueryClient()
  const [key, setKey] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)

  const statusQuery = useQuery({
    queryKey: ['license'],
    queryFn: async () => (await apiFetch<LicenseStatusDto>('/api/license')).data ?? null,
  })

  const saveMutation = useMutation({
    mutationFn: async () => {
      const trimmed = key.trim()
      if (!trimmed) throw new Error('Вставьте лицензионный ключ')
      const res = await apiFetch<LicenseStatusDto>('/api/license', {
        method: 'PUT',
        body: JSON.stringify({ key: trimmed }),
      })
      return res.data!
    },
    onSuccess: async (data) => {
      setError(null)
      setOk(`Лицензия установлена: ${data.clubName ?? '—'}`)
      setKey('')
      await queryClient.invalidateQueries({ queryKey: ['license'] })
    },
    onError: (e) => {
      setOk(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  const removeMutation = useMutation({
    mutationFn: async () => {
      await apiFetch('/api/license', { method: 'DELETE' })
    },
    onSuccess: async () => {
      setError(null)
      setOk('Ключ удалён')
      await queryClient.invalidateQueries({ queryKey: ['license'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const s = statusQuery.data
  const usedPercent =
    s && s.maxComputers > 0 ? Math.min(100, Math.round((s.usedComputers / s.maxComputers) * 100)) : 0

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Лицензия</h1>
          <p className="muted">Срок, лимит компьютеров и включённые возможности</p>
        </div>
      </header>

      <div className="bar-layout">
        <section className="cash-card">
          <h2 className="section-title">Состояние</h2>

          {statusQuery.isLoading && <p className="muted">Загрузка…</p>}
          {statusQuery.isError && <p className="error">Не удалось получить состояние лицензии</p>}

          {s && (
            <>
              <ul className="receipt-list">
                <li>
                  <strong className={stateTone(s.state)}>{STATE_LABELS[s.state] ?? s.state}</strong>
                  <div className="muted">{s.summary}</div>
                </li>
                {s.clubName && (
                  <li>
                    <span>Клуб</span>
                    <strong>{s.clubName}</strong>
                  </li>
                )}
                {s.plan && (
                  <li>
                    <span>Тариф</span>
                    <strong>{s.plan}</strong>
                  </li>
                )}
                {s.expiresAt && (
                  <li>
                    <span>Действует до</span>
                    <strong>{new Date(s.expiresAt).toLocaleDateString()}</strong>
                    {typeof s.daysLeft === 'number' && (
                      <div className="muted">
                        {s.daysLeft >= 0 ? `осталось ${s.daysLeft} дн.` : `просрочено на ${-s.daysLeft} дн.`}
                      </div>
                    )}
                  </li>
                )}
                <li>
                  <span>Компьютеры</span>
                  <strong>
                    {s.usedComputers}
                    {s.maxComputers > 0 ? ` из ${s.maxComputers}` : ' (без лимита)'}
                  </strong>
                  {s.maxComputers > 0 && (
                    <div className="muted">
                      {usedPercent}% лимита
                      {s.usedComputers >= s.maxComputers && ' — новые ПК не регистрируются'}
                    </div>
                  )}
                </li>
              </ul>

              {s.warning && <p className={s.state === 'Active' ? 'flash' : 'error'}>{s.warning}</p>}

              <h3 className="section-title">Что разрешено сейчас</h3>
              <ul className="receipt-list">
                <li>
                  <span>Запуск сеансов</span>
                  <strong className={s.canStartSessions ? 'ok' : 'error'}>
                    {s.canStartSessions ? 'да' : 'нет'}
                  </strong>
                </li>
                <li>
                  <span>Регистрация новых ПК</span>
                  <strong className={s.canRegisterComputers ? 'ok' : 'error'}>
                    {s.canRegisterComputers ? 'да' : 'нет'}
                  </strong>
                </li>
                <li>
                  <span>Продажи и пополнения</span>
                  <strong className={s.canSell ? 'ok' : 'error'}>{s.canSell ? 'да' : 'нет'}</strong>
                </li>
              </ul>

              <h3 className="section-title">Возможности</h3>
              {s.features.length === 0 ? (
                <p className="muted">Нет включённых возможностей</p>
              ) : (
                <ul className="receipt-list">
                  {s.features.map((f) => (
                    <li key={f}>
                      <span>{FEATURE_LABELS[f] ?? f}</span>
                      <strong className="ok">включено</strong>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </section>

        <section className="cash-card">
          <h2 className="section-title">Установить ключ</h2>
          <div className="form-stack">
            <label>
              Лицензионный ключ
              <textarea
                value={key}
                rows={5}
                spellCheck={false}
                onChange={(e) => setKey(e.target.value)}
                placeholder="SHIFT1.…"
              />
            </label>
            <button type="button" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate()}>
              Применить ключ
            </button>
            {ok && <p className="flash">{ok}</p>}
            {error && <p className="error">{error}</p>}

            <p className="muted">
              Ключ выдаёт поставщик системы. Он подписан и содержит срок, лимит ПК и список возможностей —
              изменить его на стороне клуба нельзя. Для продления пришлют новый ключ, его нужно вставить сюда.
            </p>

            {s && s.state !== 'Missing' && (
              <button
                type="button"
                className="ghost"
                disabled={removeMutation.isPending}
                onClick={() => {
                  if (confirm('Удалить лицензионный ключ? Клуб перейдёт в режим без лицензии.')) {
                    removeMutation.mutate()
                  }
                }}
              >
                Удалить ключ
              </button>
            )}
          </div>
        </section>
      </div>
    </>
  )
}
