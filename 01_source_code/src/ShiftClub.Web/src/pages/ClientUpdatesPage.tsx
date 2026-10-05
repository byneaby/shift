import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiFetch, getToken } from '../api/client'

type Manifest = {
  version: string
  channel: string
  packageFile: string
  sha256: string
  releaseNotes?: string
  publishedAt: string
}

export function ClientUpdatesPage() {
  const queryClient = useQueryClient()
  const [version, setVersion] = useState('')
  const [notes, setNotes] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [ok, setOk] = useState<string | null>(null)

  const currentQuery = useQuery({
    queryKey: ['client-updates-current'],
    queryFn: async () => (await apiFetch<Manifest | null>('/api/client-updates/current')).data ?? null,
  })

  const publishMutation = useMutation({
    mutationFn: async () => {
      if (!file || !version.trim()) throw new Error('Укажите версию и zip-пакет')
      const token = getToken()
      const form = new FormData()
      form.append('version', version.trim())
      form.append('releaseNotes', notes)
      form.append('channel', 'stable')
      form.append('package', file)
      const res = await fetch('/api/client-updates/publish', {
        method: 'POST',
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
        body: form,
      })
      const json = await res.json()
      if (!res.ok || !json.success) throw new Error(json.message || 'Ошибка публикации')
      return json.data as Manifest
    },
    onSuccess: async (data) => {
      setError(null)
      setOk(`Опубликовано ${data.version}`)
      setFile(null)
      setVersion('')
      setNotes('')
      await queryClient.invalidateQueries({ queryKey: ['client-updates-current'] })
    },
    onError: (e) => {
      setOk(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  const m = currentQuery.data

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Обновления клиента</h1>
        </div>
      </header>

      <div className="bar-layout">
        <section className="cash-card">
          <h2 className="section-title">Текущий пакет</h2>
          {currentQuery.isLoading && <p className="muted">Загрузка…</p>}
          {m ? (
            <ul className="receipt-list">
              <li>
                <strong>v{m.version}</strong> · {m.channel}
                <div className="muted">{m.packageFile}</div>
                <div className="muted">SHA256: {m.sha256}</div>
                {m.releaseNotes && <div>{m.releaseNotes}</div>}
                <div className="muted">{new Date(m.publishedAt).toLocaleString()}</div>
              </li>
            </ul>
          ) : (
            <p className="muted">Нет пакета</p>
          )}
        </section>

        <section className="cash-card">
          <h2 className="section-title">Опубликовать</h2>
          <div className="form-stack">
            <label>
              Версия
              <input value={version} onChange={(e) => setVersion(e.target.value)} placeholder="0.6.1" />
            </label>
            <label>
              Заметки
              <input value={notes} onChange={(e) => setNotes(e.target.value)} />
            </label>
            <label>
              Файл (.zip)
              <input
                type="file"
                accept=".zip"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </label>
            <button type="button" disabled={publishMutation.isPending} onClick={() => publishMutation.mutate()}>
              Загрузить
            </button>
            {ok && <p className="flash">{ok}</p>}
            {error && <p className="error">{error}</p>}
          </div>
        </section>
      </div>
    </>
  )
}
