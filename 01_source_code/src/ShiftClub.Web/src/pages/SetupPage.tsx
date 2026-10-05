import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiFetch } from '../api/client'
import type { ApplySetupResultDto, SetupStatusDto } from '../api/client'

type Form = {
  clubName: string
  shortName: string
  timeZoneId: string
  currencyCode: string
  address: string
  ownerPassword: string
  ownerPasswordRepeat: string
  licenseKey: string
}

const EMPTY: Form = {
  clubName: '',
  shortName: '',
  timeZoneId: '',
  currencyCode: '',
  address: '',
  ownerPassword: '',
  ownerPasswordRepeat: '',
  licenseKey: '',
}

export function SetupPage() {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<Form>(EMPTY)
  const [markCompleted, setMarkCompleted] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ApplySetupResultDto | null>(null)

  const status = useQuery({
    queryKey: ['setup'],
    queryFn: async () => (await apiFetch<SetupStatusDto>('/api/setup')).data ?? null,
  })

  const apply = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<ApplySetupResultDto>('/api/setup', {
        method: 'POST',
        body: JSON.stringify({
          clubName: form.clubName.trim() || null,
          shortName: form.shortName.trim() || null,
          timeZoneId: form.timeZoneId.trim() || null,
          currencyCode: form.currencyCode.trim() || null,
          address: form.address.trim() || null,
          ownerPassword: form.ownerPassword || null,
          licenseKey: form.licenseKey.trim() || null,
          markCompleted,
        }),
      })
      return res.data!
    },
    onSuccess: async (data) => {
      setError(null)
      setResult(data)
      setForm((f) => ({ ...f, ownerPassword: '', ownerPasswordRepeat: '', licenseKey: '' }))
      await queryClient.invalidateQueries({ queryKey: ['setup'] })
      await queryClient.invalidateQueries({ queryKey: ['branding'] })
      await queryClient.invalidateQueries({ queryKey: ['license'] })
    },
    onError: (e) => {
      setResult(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  function set<K extends keyof Form>(key: K, value: Form[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  function submit() {
    if (form.ownerPassword && form.ownerPassword !== form.ownerPasswordRepeat) {
      setResult(null)
      setError('Пароли не совпадают.')
      return
    }
    apply.mutate()
  }

  const data = status.data

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Настройка клуба</h1>
          <p className="muted">
            Что осталось сделать, чтобы клуб работал под своим именем и не потерял базу
          </p>
        </div>
      </header>

      <div className="bar-layout">
        <section className="cash-card">
          <h2 className="section-title">Чек-лист</h2>
          {status.isLoading && <p className="muted">Загрузка…</p>}
          {data && (
            <>
              <p className={data.required ? 'error' : 'flash'}>{data.summary}</p>
              <p className="muted">
                Выполнено {data.doneCount} из {data.totalCount}
              </p>
              <ul className="setup-steps">
                {data.steps.map((step) => (
                  <li key={step.code} className={step.done ? 'setup-step done' : 'setup-step'}>
                    <span className="setup-mark">{step.done ? '✓' : '•'}</span>
                    <div>
                      <strong>{step.title}</strong>
                      {step.required && !step.done && <span className="setup-required"> обязательно</span>}
                      <div className="muted">{step.hint}</div>
                    </div>
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>

        <section className="cash-card">
          <h2 className="section-title">Заполнить</h2>
          <div className="form-stack">
            <label>
              Название клуба
              <input
                value={form.clubName}
                maxLength={80}
                onChange={(e) => set('clubName', e.target.value)}
                placeholder="Nexus Arena"
              />
              <span className="muted">Подставится в панель, на табло, в Shell и в сообщения бота</span>
            </label>
            <label>
              Короткое название
              <input
                value={form.shortName}
                maxLength={24}
                onChange={(e) => set('shortName', e.target.value)}
                placeholder="NEXUS"
              />
            </label>
            <label>
              Город и адрес
              <input
                value={form.address}
                onChange={(e) => set('address', e.target.value)}
                placeholder="г. Астана, ул. Кенесары 24"
              />
            </label>
            <label>
              Часовой пояс
              <input
                value={form.timeZoneId}
                onChange={(e) => set('timeZoneId', e.target.value)}
                placeholder="Asia/Almaty"
              />
              <span className="muted">
                От него считаются смены и ночные тарифы. На Windows подходит и «Central Asia Standard Time»
              </span>
            </label>
            <label>
              Валюта
              <input
                value={form.currencyCode}
                maxLength={3}
                onChange={(e) => set('currencyCode', e.target.value.toUpperCase())}
                placeholder="KZT"
              />
            </label>

            <label>
              Новый пароль владельца
              <input
                type="password"
                autoComplete="new-password"
                value={form.ownerPassword}
                onChange={(e) => set('ownerPassword', e.target.value)}
              />
              <span className="muted">Минимум 10 символов. Пароль из поставки знает любой, кто читал инструкцию</span>
            </label>
            <label>
              Повторите пароль
              <input
                type="password"
                autoComplete="new-password"
                value={form.ownerPasswordRepeat}
                onChange={(e) => set('ownerPasswordRepeat', e.target.value)}
              />
            </label>

            <label>
              Лицензионный ключ
              <textarea
                rows={3}
                value={form.licenseKey}
                onChange={(e) => set('licenseKey', e.target.value)}
                placeholder="SHIFT1.…"
              />
            </label>

            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={markCompleted}
                onChange={(e) => setMarkCompleted(e.target.checked)}
              />
              Отметить настройку завершённой и убрать напоминание
            </label>

            <button type="button" disabled={apply.isPending} onClick={submit}>
              {apply.isPending ? 'Применяю…' : 'Применить'}
            </button>

            {error && <p className="error">{error}</p>}
            {result?.applied.map((line) => (
              <p className="flash" key={line}>
                {line}
              </p>
            ))}
            {result?.problems.map((line) => (
              <p className="error" key={line}>
                {line}
              </p>
            ))}
          </div>
        </section>
      </div>
    </>
  )
}
