import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiFetch, setToken } from '../api/client'
import type { LoginResponse } from '../api/client'
import { useApplyBranding, useBranding } from '../branding'

type StaffTicket = {
  ticketId: string
  code: string
  deepLink: string
  qrPngBase64: string
  expiresAt: string
}

type StaffTicketStatus = {
  status: string
  auth?: LoginResponse | null
  message?: string | null
}

const NONCE_KEY = 'shiftclub.staffQrNonce'

function getOrCreateNonce(): string {
  let n = sessionStorage.getItem(NONCE_KEY)
  if (!n || n.length < 16) {
    n = crypto.randomUUID().replace(/-/g, '') + crypto.randomUUID().replace(/-/g, '').slice(0, 8)
    sessionStorage.setItem(NONCE_KEY, n)
  }
  return n
}

export function LoginPage() {
  const navigate = useNavigate()
  const branding = useBranding()
  useApplyBranding(branding)
  const [login, setLogin] = useState('owner')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [ticket, setTicket] = useState<StaffTicket | null>(null)
  const [expired, setExpired] = useState(false)
  const [tgHint, setTgHint] = useState('Сканируйте QR в Telegram')
  const [tgBusy, setTgBusy] = useState(false)
  const pollRef = useRef<number | null>(null)
  const ticketRef = useRef<StaffTicket | null>(null)
  const nonceRef = useRef(getOrCreateNonce())

  function finishLogin(result: LoginResponse) {
    setToken(result.accessToken)
    localStorage.setItem('shiftclub.employee', JSON.stringify(result.employee))
    navigate('/')
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const result = await apiFetch<LoginResponse>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ login, password }),
      })
      finishLogin(result.data!)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ошибка входа')
    } finally {
      setLoading(false)
    }
  }

  function stopPoll() {
    if (pollRef.current != null) {
      window.clearInterval(pollRef.current)
      pollRef.current = null
    }
  }

  async function cancelCurrentTicket() {
    const t = ticketRef.current
    if (!t) return
    try {
      await apiFetch(`/api/auth/telegram/cancel/${t.ticketId}?clientNonce=${encodeURIComponent(nonceRef.current)}`, {
        method: 'POST',
      })
    } catch {
      /* ignore */
    }
  }

  async function startTelegramQr() {
    setError(null)
    setTgBusy(true)
    setExpired(false)
    stopPoll()
    try {
      const res = await apiFetch<StaffTicket>('/api/auth/telegram/start', {
        method: 'POST',
        body: JSON.stringify({ clientNonce: nonceRef.current }),
      })
      const t = res.data!
      ticketRef.current = t
      setTicket(t)
      setTgHint(`Код ${t.code} · откройте бота или сканируйте QR`)
      pollRef.current = window.setInterval(async () => {
        try {
          const st = await apiFetch<StaffTicketStatus>(
            `/api/auth/telegram/status/${t.ticketId}?clientNonce=${encodeURIComponent(nonceRef.current)}`,
          )
          const d = st.data!
          if (d.status === 'Consumed' && d.auth) {
            stopPoll()
            setTgHint(d.message || 'Вход подтверждён')
            finishLogin(d.auth)
            return
          }
          if (d.status === 'Expired' || d.status === 'Cancelled') {
            stopPoll()
            setTgHint(d.message || 'Код истёк — нажмите обновить')
            setTicket(null)
            ticketRef.current = null
            setExpired(true)
          } else if (d.message) {
            setTgHint(d.message)
          }
        } catch {
          /* keep polling */
        }
      }, 2000)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Не удалось создать QR')
      setTicket(null)
      ticketRef.current = null
      setExpired(true)
    } finally {
      setTgBusy(false)
    }
  }

  useEffect(() => {
    void startTelegramQr()
    return () => {
      stopPoll()
      void cancelCurrentTicket()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div className="login-shell">
      <div className="login-layout">
        <form className="login-card" onSubmit={onSubmit} data-allow-autocomplete>
          {branding.logoUrl ? (
            <img className="brand-logo" src={branding.logoUrl} alt={branding.clubName} />
          ) : null}
          <p className="brand">{branding.clubName}</p>
          <h1>Вход в панель</h1>
          <p className="muted" style={{ margin: '-6px 0 4px' }}>
            Касса, зал и управление клубом
          </p>

          <label>
            Логин или телефон
            <input
              value={login}
              onChange={(e) => setLogin(e.target.value)}
              autoComplete="username"
              required
            />
          </label>
          <label>
            Пароль
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </label>

          {error && <p className="error">{error}</p>}

          <button type="submit" disabled={loading}>
            {loading ? 'Вход…' : 'Войти'}
          </button>
        </form>

        <div className="login-card login-tg">
          <p className="brand">Telegram</p>
          <h2 style={{ margin: 0, fontSize: '1.15rem' }}>Вход по QR</h2>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Откройте бота клуба и подтвердите вход. Telegram должен быть привязан к сотруднику.
          </p>
          <div className="login-qr-box">
            {ticket?.qrPngBase64 && !expired ? (
              <img
                src={`data:image/png;base64,${ticket.qrPngBase64}`}
                alt="QR для входа"
                width={200}
                height={200}
              />
            ) : (
              <div className="login-qr-placeholder">
                {tgBusy ? 'Загрузка…' : expired ? 'QR истёк' : 'Нет QR'}
                {expired && (
                  <button
                    type="button"
                    className="ghost"
                    style={{ marginTop: 12 }}
                    disabled={tgBusy}
                    onClick={() => void startTelegramQr()}
                  >
                    Обновить QR
                  </button>
                )}
              </div>
            )}
          </div>
          <p className="muted" style={{ margin: 0, fontSize: 13, textAlign: 'center' }}>
            {tgHint}
          </p>
          {ticket?.deepLink && !expired && (
            <a className="login-deeplink" href={ticket.deepLink} target="_blank" rel="noreferrer">
              Открыть в Telegram
            </a>
          )}
          {!expired && (
            <button type="button" className="ghost" disabled={tgBusy} onClick={() => void startTelegramQr()}>
              Обновить QR
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
