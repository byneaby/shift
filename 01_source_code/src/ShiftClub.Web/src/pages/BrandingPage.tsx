import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import type { BrandingDto } from '../branding'

type Form = {
  clubName: string
  shortName: string
  logoUrl: string
  accentColor: string
  websiteUrl: string
  supportContact: string
  telegramSignature: string
}

const EMPTY: Form = {
  clubName: '',
  shortName: '',
  logoUrl: '',
  accentColor: '#ff6a00',
  websiteUrl: '',
  supportContact: '',
  telegramSignature: '',
}

export function BrandingPage() {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<Form>(EMPTY)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['branding', 'admin'],
    queryFn: async () => (await apiFetch<BrandingDto>('/api/branding')).data ?? null,
  })

  useEffect(() => {
    const d = query.data
    if (!d) return
    setForm({
      clubName: d.clubName,
      shortName: d.shortName,
      logoUrl: d.logoUrl ?? '',
      accentColor: d.accentColor,
      websiteUrl: d.websiteUrl ?? '',
      supportContact: d.supportContact ?? '',
      telegramSignature: d.telegramSignature,
    })
  }, [query.data])

  const save = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<BrandingDto>('/api/branding', {
        method: 'PUT',
        body: JSON.stringify(form),
      })
      return res.data!
    },
    onSuccess: async () => {
      setError(null)
      setOk('Сохранено. Название и цвет применятся на всех экранах в течение пары минут.')
      await queryClient.invalidateQueries({ queryKey: ['branding'] })
    },
    onError: (e) => {
      setOk(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  function set<K extends keyof Form>(key: K, value: Form[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Название и оформление</h1>
          <p className="muted">Как клуб называет себя в панели, на табло, в Shell и в сообщениях бота</p>
        </div>
      </header>

      <div className="bar-layout">
        <section className="cash-card">
          <h2 className="section-title">Клуб</h2>
          {query.isLoading && <p className="muted">Загрузка…</p>}
          <div className="form-stack">
            <label>
              Название клуба
              <input
                value={form.clubName}
                maxLength={80}
                onChange={(e) => set('clubName', e.target.value)}
                placeholder="Nexus Arena"
              />
            </label>
            <label>
              Короткое название
              <input
                value={form.shortName}
                maxLength={24}
                onChange={(e) => set('shortName', e.target.value)}
                placeholder="NEXUS"
              />
              <span className="muted">Для табло в зале и узкого экрана на кассе</span>
            </label>
            <label>
              Подпись в сообщениях Telegram
              <input
                value={form.telegramSignature}
                maxLength={80}
                onChange={(e) => set('telegramSignature', e.target.value)}
                placeholder="Nexus Arena"
              />
              <span className="muted">Этим именем бот подписывает рассылки и напоминания</span>
            </label>
          </div>
        </section>

        <section className="cash-card">
          <h2 className="section-title">Оформление</h2>
          <div className="form-stack">
            <label>
              Акцентный цвет
              <input
                type="color"
                value={form.accentColor}
                onChange={(e) => set('accentColor', e.target.value)}
              />
            </label>
            <label>
              Ссылка на логотип
              <input
                value={form.logoUrl}
                onChange={(e) => set('logoUrl', e.target.value)}
                placeholder="/media/branding/logo.png"
              />
              <span className="muted">
                Файл можно положить в папку data/branding на сервере — она отдаётся по /media/branding
              </span>
            </label>
            <label>
              Сайт клуба
              <input
                value={form.websiteUrl}
                onChange={(e) => set('websiteUrl', e.target.value)}
                placeholder="https://myclub.kz"
              />
            </label>
            <label>
              Контакт для гостей
              <input
                value={form.supportContact}
                onChange={(e) => set('supportContact', e.target.value)}
                placeholder="@myclub_support"
              />
            </label>

            <button type="button" disabled={save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? 'Сохраняю…' : 'Сохранить'}
            </button>
            {ok && <p className="flash">{ok}</p>}
            {error && <p className="error">{error}</p>}
          </div>
        </section>
      </div>
    </>
  )
}
