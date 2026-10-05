import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../api/client'
import type { ServerUpdateStartResultDto, ServerUpdateStatusDto, SystemStatusDto } from '../api/client'

function dateTime(value: string) {
  return new Date(value).toLocaleString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function megabytes(bytes: number) {
  return `${(bytes / 1024 / 1024).toFixed(1)} МБ`
}

const RUN_STATES: Record<string, string> = {
  swapping: 'шла подмена файлов',
  ok: 'установлено',
  rolled_back: 'не поднялось, вернули прежнюю версию',
  failed: 'не удалось',
}

export function SystemPage() {
  const queryClient = useQueryClient()

  const status = useQuery({
    queryKey: ['system', 'status'],
    queryFn: async () => (await apiFetch<SystemStatusDto>('/api/system/status')).data ?? null,
    refetchInterval: 60_000,
  })

  const clearErrors = useMutation({
    mutationFn: async () => {
      await apiFetch('/api/system/errors/clear', { method: 'POST' })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['system'] })
    },
  })

  const update = useQuery({
    queryKey: ['system', 'update'],
    queryFn: async () => (await apiFetch<ServerUpdateStatusDto>('/api/system/update')).data ?? null,
  })

  const [updateNote, setUpdateNote] = useState<string | null>(null)

  const checkUpdate = useMutation({
    mutationFn: async () =>
      (await apiFetch<ServerUpdateStatusDto>('/api/system/update/check', { method: 'POST' })).data ?? null,
    onSuccess: async (result) => {
      setUpdateNote(
        result?.updateAvailable
          ? `Вышла версия ${result.latestVersion}.`
          : result?.problem ?? 'Установлена актуальная версия.',
      )
      await queryClient.invalidateQueries({ queryKey: ['system', 'update'] })
    },
    onError: () => setUpdateNote('Не удалось связаться с каналом обновлений.'),
  })

  const startUpdate = useMutation({
    mutationFn: async () =>
      (
        await apiFetch<ServerUpdateStartResultDto>('/api/system/update/start', {
          method: 'POST',
          body: JSON.stringify({ version: null }),
        })
      ).data ?? null,
    onSuccess: (result) => setUpdateNote(result?.message ?? 'Обновление запущено.'),
    onError: (error: unknown) =>
      setUpdateNote(error instanceof Error ? error.message : 'Обновление не запустилось.'),
  })

  const data = status.data
  const upd = update.data

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Состояние системы</h1>
          <p className="muted">
            Что назвать поддержке по телефону: версия, база, копии, лицензия и последние ошибки
          </p>
        </div>
      </header>

      {status.isLoading && <p className="muted">Загрузка…</p>}
      {status.error && <p className="error">Не удалось получить состояние сервера.</p>}

      {data && (
        <div className="bar-layout">
          <section className="cash-card">
            <h2 className="section-title">Проверки</h2>
            <p className={data.ok ? 'flash' : 'error'}>
              {data.ok ? 'Всё в порядке' : 'Есть замечания — смотрите список ниже'}
            </p>
            <ul className="setup-steps">
              {data.checks.map((check) => (
                <li key={check.code} className={check.ok ? 'setup-step done' : 'setup-step'}>
                  <span className="setup-mark">{check.ok ? '✓' : '!'}</span>
                  <div>
                    <strong>{check.title}</strong> — {check.value}
                    {check.problem && <div className="error">{check.problem}</div>}
                  </div>
                </li>
              ))}
            </ul>
          </section>

          <section className="cash-card">
            <h2 className="section-title">Сервер</h2>
            <dl className="kv-list">
              <dt>Клуб</dt>
              <dd>{data.clubName}</dd>
              <dt>Версия</dt>
              <dd>{data.version}</dd>
              <dt>Лицензия</dt>
              <dd>
                {data.licenseState}
                {data.licenseClub ? ` — ${data.licenseClub}` : ''}
              </dd>
              <dt>Работает без перезапуска</dt>
              <dd>{data.uptime}</dd>
              <dt>Время клуба</dt>
              <dd>
                {data.clubTimeLocal} ({data.clubTimeZone})
              </dd>
              <dt>Машина</dt>
              <dd>
                {data.machineName} / {data.environment}
              </dd>
            </dl>
          </section>
        </div>
      )}

      {upd && (
        <section className="cash-card" style={{ marginTop: 16 }}>
          <h2 className="section-title">Версия сервера</h2>
          <dl className="kv-list">
            <dt>Установлено</dt>
            <dd>{upd.currentVersion}</dd>
            {upd.latestVersion && (
              <>
                <dt>Вышло</dt>
                <dd>
                  {upd.latestVersion}
                  {upd.publishedAt ? ` от ${dateTime(upd.publishedAt)}` : ''}
                  {upd.sizeBytes > 0 ? `, ${megabytes(upd.sizeBytes)}` : ''}
                </dd>
              </>
            )}
            {upd.lastRun && (
              <>
                <dt>Прошлое обновление</dt>
                <dd>
                  {upd.lastRun.version} — {RUN_STATES[upd.lastRun.state] ?? upd.lastRun.state}
                  {upd.lastRun.message ? `: ${upd.lastRun.message}` : ''}
                </dd>
              </>
            )}
          </dl>

          {upd.releaseNotes && <p>{upd.releaseNotes}</p>}
          {upd.problem && <p className="muted">{upd.problem}</p>}

          {upd.updateAvailable && (
            <p className="muted">
              Перед обновлением сервер сам сделает копию базы. Пока идёт подмена, касса и ПК работать не
              будут — несколько минут. Если новая версия не поднимется, вернётся прежняя.
            </p>
          )}

          <div className="order-actions">
            <button type="button" disabled={checkUpdate.isPending} onClick={() => checkUpdate.mutate()}>
              Проверить обновления
            </button>
            {upd.updateAvailable && (
              <button
                type="button"
                className="primary"
                disabled={startUpdate.isPending}
                onClick={() => {
                  if (window.confirm(`Обновить сервер до версии ${upd.latestVersion}? Клуб остановится на несколько минут.`)) {
                    startUpdate.mutate()
                  }
                }}
              >
                Обновить до {upd.latestVersion}
              </button>
            )}
          </div>

          {updateNote && <p className="flash">{updateNote}</p>}
        </section>
      )}

      {data && (
        <section className="cash-card" style={{ marginTop: 16 }}>
          <h2 className="section-title">Ошибки за сутки: {data.errorsLastDay}</h2>
          {data.recentErrors.length === 0 ? (
            <p className="muted">Ошибок не было. Список живёт в памяти и обнуляется при перезапуске сервера.</p>
          ) : (
            <>
              <p className="muted">
                Телефоны, адреса почты, пароли и токены из текстов вырезаны — такой список можно показывать
                поставщику.
              </p>
              <table className="reports-table">
                <thead>
                  <tr>
                    <th>Когда</th>
                    <th>Где</th>
                    <th>Что</th>
                    <th>Раз</th>
                  </tr>
                </thead>
                <tbody>
                  {data.recentErrors.map((group) => (
                    <tr key={group.fingerprint}>
                      <td>{dateTime(group.lastAt)}</td>
                      <td>{group.source}</td>
                      <td>
                        <strong>{group.kind}</strong>
                        <div className="muted">{group.message}</div>
                        {group.where && <div className="muted">{group.where}</div>}
                      </td>
                      <td>{group.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <button type="button" disabled={clearErrors.isPending} onClick={() => clearErrors.mutate()}>
                Очистить список
              </button>
            </>
          )}
        </section>
      )}
    </>
  )
}
