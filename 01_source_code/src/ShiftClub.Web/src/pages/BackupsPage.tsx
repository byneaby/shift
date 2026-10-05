import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiFetch, getToken } from '../api/client'
import type { BackupStatusDto, RunBackupResultDto } from '../api/client'

const KIND_LABELS: Record<string, string> = {
  manual: 'вручную',
  daily: 'по расписанию',
  'pre-migration': 'перед обновлением базы',
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} Б`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} КБ`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} МБ`
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} ГБ`
}

export function BackupsPage() {
  const queryClient = useQueryClient()
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const statusQuery = useQuery({
    queryKey: ['backups'],
    queryFn: async () => (await apiFetch<BackupStatusDto>('/api/backups')).data ?? null,
    refetchInterval: 60_000,
  })

  const runMutation = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<RunBackupResultDto>('/api/backups/run', { method: 'POST' })
      return res.data!
    },
    onSuccess: async (data) => {
      if (data.success) {
        setError(null)
        setMessage(data.message)
      } else {
        setMessage(null)
        setError(data.message)
      }
      await queryClient.invalidateQueries({ queryKey: ['backups'] })
    },
    onError: (e) => {
      setMessage(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  async function download(fileName: string) {
    setError(null)
    try {
      const token = getToken()
      const res = await fetch(`/api/backups/download/${encodeURIComponent(fileName)}`, {
        headers: token ? { Authorization: `Bearer ${token}` } : undefined,
      })
      if (!res.ok) throw new Error('Файл не отдался')
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = fileName
      link.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось скачать файл')
    }
  }

  const s = statusQuery.data

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Копии базы</h1>
          <p className="muted">Автоматические копии, ручная копия и скачивание файла</p>
        </div>
      </header>

      <div className="bar-layout">
        <section className="cash-card">
          <h2 className="section-title">Состояние</h2>

          {statusQuery.isLoading && <p className="muted">Загрузка…</p>}
          {statusQuery.isError && <p className="error">Не удалось получить состояние копий</p>}

          {s && (
            <>
              <ul className="receipt-list">
                <li>
                  <span>Автоматические копии</span>
                  <strong className={s.enabled ? 'ok' : 'error'}>{s.enabled ? 'включены' : 'выключены'}</strong>
                  {s.enabled && <div className="muted">каждый день в {s.dailyHourLocal}:00</div>}
                </li>
                <li>
                  <span>pg_dump</span>
                  <strong className={s.toolAvailable ? 'ok' : 'error'}>
                    {s.toolAvailable ? 'найден' : 'не найден'}
                  </strong>
                  {s.toolPath && <div className="muted">{s.toolPath}</div>}
                </li>
                <li>
                  <span>Папка с копиями</span>
                  <strong>{s.directory}</strong>
                  <div className="muted">храним {s.keepDays} дн., всего {formatSize(s.totalSizeBytes)}</div>
                </li>
                <li>
                  <span>Последняя удачная копия</span>
                  <strong>
                    {s.lastSuccessAt ? new Date(s.lastSuccessAt).toLocaleString() : 'за время работы сервера нет'}
                  </strong>
                </li>
              </ul>

              {s.warning && <p className="error">{s.warning}</p>}

              <div className="form-stack">
                <button type="button" disabled={runMutation.isPending} onClick={() => runMutation.mutate()}>
                  {runMutation.isPending ? 'Делаю копию…' : 'Сделать копию сейчас'}
                </button>
                {message && <p className="flash">{message}</p>}
                {error && <p className="error">{error}</p>}
              </div>

              <p className="muted">
                Копия создаётся через pg_dump в формате custom. Восстановление:
                <code> pg_restore --clean --if-exists -d shiftclub файл.dump</code>. Копию перед обновлением
                базы сервер делает сам — если она не получилась, обновление не запускается.
              </p>
            </>
          )}
        </section>

        <section className="cash-card">
          <h2 className="section-title">Файлы</h2>
          {s && s.files.length === 0 && <p className="muted">Копий пока нет</p>}
          {s && s.files.length > 0 && (
            <ul className="receipt-list">
              {s.files.map((f) => (
                <li key={f.fileName}>
                  <span>{new Date(f.createdAt).toLocaleString()}</span>
                  <strong>{formatSize(f.sizeBytes)}</strong>
                  <div className="muted">{KIND_LABELS[f.kind] ?? f.kind}</div>
                  <button type="button" className="ghost" onClick={() => download(f.fileName)}>
                    Скачать
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </>
  )
}
