import { useState } from 'react'
import type { FormEvent } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import {
  daysPhrase,
  formatLicenseWhen,
  kindLabel,
  licenseStateLabel,
  licenseStatusKey,
  licenseTone,
  seatsLabel,
  useLicenseStatus,
} from '../licenseStatus'
import type { LicenseStatus } from '../licenseStatus'
import { can, Perm } from '../permissions'

const KEY_SHAPE = /^SHIFT-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$/

function normalizeKey(raw: string): string {
  return raw.trim().toUpperCase().replace(/[\s\t\r\n]/g, '')
}

export function LicenseHomeCard() {
  const query = useLicenseStatus()
  const status = query.data
  if (query.isLoading) {
    return (
      <section className="license-home license-home--off" aria-busy="true">
        <p className="muted">Смотрим лицензию…</p>
      </section>
    )
  }
  if (!status) {
    return (
      <section className="license-home license-home--bad">
        <p>Не удалось прочитать лицензию.</p>
        <Link to="/license">Открыть лицензию</Link>
      </section>
    )
  }

  const tone = licenseTone(status)
  return (
    <Link to="/license" className={`license-home license-home--${tone}`}>
      <span className="license-home__label">Лицензия</span>
      <strong>{homeTitle(status)}</strong>
      <span className="muted">{homeDetail(status)}</span>
    </Link>
  )
}

function homeTitle(status: LicenseStatus): string {
  if (!status.enforce) return 'Проверка на этом сервере выключена'
  if (!status.operational) return 'Сервер остановлен'
  if (status.daysRemaining == null) return licenseStateLabel(status)
  if (status.warning) return `Скоро закончится · ещё ${daysPhrase(status.daysRemaining)}`
  return `Работает · ещё ${daysPhrase(status.daysRemaining)}`
}

function homeDetail(status: LicenseStatus): string {
  if (!status.enforce) return 'Зал работает без срока. На боевом клубе так не оставляют.'
  if (!status.operational) return status.message || 'Нужен действующий ключ, чтобы снова открыть зал.'
  const until = status.expiresAt ? `до ${formatLicenseWhen(status.expiresAt)}` : 'срок не указан'
  const club = status.club ? `${status.club} · ` : ''
  const seats =
    status.maxComputers == null
      ? 'без лимита компьютеров'
      : `${status.usedComputers} из ${status.maxComputers} компьютеров`
  return `${club}${kindLabel(status.kind)} · ${until} · ${seats}`
}

export function LicensePage() {
  const query = useLicenseStatus()
  const status = query.data
  const mayReplace = can(Perm.SettingsManage)

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Лицензия</h1>
          <p className="muted license-lead">
            Срок, на который этот сервер клуба может работать. Пока лицензия действует, касса, зал и компьютеры
            открываются как обычно.
          </p>
        </div>
        {status && <span className={`chip license-state license-state--${licenseTone(status)}`}>{licenseStateLabel(status)}</span>}
      </header>

      {query.isLoading && <p className="muted">Загружаем срок и места…</p>}
      {query.isError && <p className="error">{(query.error as Error).message}</p>}

      {status && !status.enforce && (
        <section className="panel license-callout">
          <h2>Проверка выключена</h2>
          <p>
            На этом сервере срок лицензии не проверяется, зал работает без ограничения. Так оставляют только для
            разработки. У клуба проверка должна быть включена.
          </p>
        </section>
      )}

      {status && status.enforce && !status.operational && (
        <section className="panel license-callout license-callout--bad">
          <h2>Сервер остановлен</h2>
          <p>{status.message || 'Нет действующей лицензии.'}</p>
          <p>
            Касса, панель и компьютеры в зале не откроют новую работу. Данные клуба на месте. Чтобы снова включить
            зал, нужен действующий ключ.
          </p>
        </section>
      )}

      {status && status.enforce && status.operational && status.warning && (
        <section className="panel license-callout license-callout--warn">
          <h2>Срок подходит к концу</h2>
          <p>
            Лицензия закончится {status.expiresAt ? formatLicenseWhen(status.expiresAt) : 'скоро'}
            {status.daysRemaining != null ? ` — ещё ${daysPhrase(status.daysRemaining)}` : ''}. Если её не продлить,
            сервер остановится.
          </p>
        </section>
      )}

      {status && status.enforce && (
        <section className="license-facts" aria-label="Срок и места">
          <article className="license-fact">
            <span>Клуб</span>
            <strong>{status.club || '—'}</strong>
          </article>
          <article className="license-fact">
            <span>Вид</span>
            <strong>{kindLabel(status.kind)}</strong>
          </article>
          <article className="license-fact">
            <span>Действует до</span>
            <strong>{formatLicenseWhen(status.expiresAt)}</strong>
            <small>Кызылорда, UTC+5</small>
          </article>
          <article className="license-fact">
            <span>Осталось</span>
            <strong>{status.daysRemaining == null ? '—' : daysPhrase(status.daysRemaining)}</strong>
          </article>
          <article className="license-fact">
            <span>Компьютеры</span>
            <strong>{seatsLabel(status)}</strong>
            <small>Одобренные места. Ожидающие подтверждения место не занимают.</small>
          </article>
        </section>
      )}

      {status && status.enforce && status.operational && (
        <section className="panel license-note">
          <h2>Как это работает</h2>
          <p>
            Если срок выйдет, этот сервер остановится: панель, касса и компьютеры не откроют новую работу. База клуба
            не удаляется. Зал снова включится, когда появится действующая лицензия.
          </p>
          <p>
            Продление того же ключа сервер увидит сам, примерно за {status.refreshMinutes} мин. Вставлять ключ ещё раз
            не нужно.
          </p>
          <p>Новый ключ нужен только если старый отозвали и выдали другой.</p>
          {status.maxComputers != null && (
            <p>
              Лимит — {status.maxComputers} компьютеров. Сверх него новые ПК не подтверждаются. Уже одобренные из зала
              не выключаются.
            </p>
          )}
        </section>
      )}

      {status && mayReplace && <ReplaceKey refreshMinutes={status.refreshMinutes} />}
      {status && !mayReplace && (
        <p className="muted">Сменить ключ может владелец или сотрудник с доступом к настройкам.</p>
      )}
    </>
  )
}

function ReplaceKey({ refreshMinutes }: { refreshMinutes: number }) {
  const queryClient = useQueryClient()
  const [key, setKey] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setDone(null)
    const normalized = normalizeKey(key)
    if (!KEY_SHAPE.test(normalized)) {
      setError('Ключ должен быть вида SHIFT-XXXX-XXXX-XXXX.')
      return
    }
    setBusy(true)
    try {
      const result = await apiFetch('/api/license/activate', {
        method: 'POST',
        body: JSON.stringify({ key: normalized }),
      })
      setKey('')
      setDone(result.message || 'Ключ принят.')
      await queryClient.invalidateQueries({ queryKey: licenseStatusKey })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ключ не принят')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel license-replace">
      <h2>Новый ключ</h2>
      <p className="muted">
        Вставьте сюда, если в центре лицензий выдали другой ключ. Продление текущего подхватывается само, примерно за{' '}
        {refreshMinutes} мин.
      </p>
      <form className="form-stack" onSubmit={onSubmit}>
        <label>
          Ключ
          <input
            className="license-key-input"
            value={key}
            onChange={(e) => setKey(e.target.value)}
            placeholder="SHIFT-XXXX-XXXX-XXXX"
            autoComplete="off"
            spellCheck={false}
            inputMode="text"
          />
        </label>
        {error && <p className="error">{error}</p>}
        {done && <p className="license-done">{done}</p>}
        <button type="submit" disabled={busy || normalizeKey(key).length === 0}>
          {busy ? 'Проверяем…' : 'Применить ключ'}
        </button>
      </form>
    </section>
  )
}
