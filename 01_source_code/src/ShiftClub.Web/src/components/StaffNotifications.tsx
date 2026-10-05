import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import * as signalR from '@microsoft/signalr'
import { getToken } from '../api/client'
import { buildFloorFocusHref } from '../staffNav'

export type StaffToastItem = {
  id: string
  kind: string
  title: string
  body: string
  sticky?: boolean
  at: number
  computerId?: string
  orderId?: string
}

function playAlertTone(kind: string) {
  try {
    const Ctx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
    const ctx = new Ctx()
    const now = ctx.currentTime
    const freqs = kind === 'security' ? [880, 660, 880] : kind === 'bar' ? [520, 780] : [640, 840, 640]
    freqs.forEach((freq, i) => {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'square'
      osc.frequency.value = freq
      gain.gain.setValueAtTime(0.0001, now)
      gain.gain.exponentialRampToValueAtTime(0.12, now + 0.02 + i * 0.18)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + 0.16 + i * 0.18)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start(now + i * 0.18)
      osc.stop(now + 0.2 + i * 0.18)
    })
    window.setTimeout(() => void ctx.close(), 1200)
  } catch {
    /* ignore autoplay limits */
  }
}

function kindClass(kind: string) {
  if (kind === 'security') return 'staff-toast staff-toast--security'
  if (kind === 'bar') return 'staff-toast staff-toast--bar'
  if (kind === 'help') return 'staff-toast staff-toast--help'
  return 'staff-toast'
}

function focusFromKind(kind: string): 'bar' | 'help' | 'security' | null {
  if (kind === 'bar' || kind === 'help' || kind === 'security') return kind
  return null
}

export function StaffNotifications() {
  const navigate = useNavigate()
  const [items, setItems] = useState<StaffToastItem[]>([])
  const idRef = useRef(0)

  const push = useCallback(
    (
      toast: Omit<StaffToastItem, 'id' | 'at'> & {
        sticky?: boolean
        sound?: boolean
        computerId?: string
        orderId?: string
      },
    ) => {
      const id = `t-${++idRef.current}`
      const item: StaffToastItem = {
        id,
        kind: toast.kind,
        title: toast.title,
        body: toast.body,
        sticky: toast.sticky ?? true,
        at: Date.now(),
        computerId: toast.computerId,
        orderId: toast.orderId,
      }
      setItems((prev) => [item, ...prev].slice(0, 6))
      if (toast.sound !== false) playAlertTone(toast.kind)
      if (!item.sticky) {
        window.setTimeout(() => {
          setItems((prev) => prev.filter((x) => x.id !== id))
        }, 20000)
      }
    },
    [],
  )

  const dismiss = useCallback((id: string) => {
    setItems((prev) => prev.filter((x) => x.id !== id))
  }, [])

  const openToast = useCallback(
    (item: StaffToastItem) => {
      const focus = focusFromKind(item.kind)
      if (!focus && !item.computerId && !item.orderId) return
      navigate(
        buildFloorFocusHref({
          focus: focus ?? (item.orderId ? 'bar' : 'help'),
          computerId: item.computerId,
          orderId: item.orderId,
        }),
      )
      dismiss(item.id)
    },
    [dismiss, navigate],
  )

  useEffect(() => {
    const token = getToken()
    if (!token) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/staff?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()

    connection.on(
      'StaffToast',
      (payload: {
        kind?: string
        title?: string
        body?: string
        sound?: boolean
        sticky?: boolean
        computerId?: string
        orderId?: string
      }) => {
        push({
          kind: payload.kind ?? 'info',
          title: payload.title ?? 'Уведомление',
          body: payload.body ?? '',
          sound: payload.sound !== false,
          sticky: payload.sticky !== false,
          computerId: payload.computerId,
          orderId: payload.orderId,
        })
      },
    )

    void connection.start()
    return () => {
      void connection.stop()
    }
  }, [push])

  if (items.length === 0) return null

  return (
    <div className="staff-toast-stack" aria-live="assertive">
      {items.map((item) => {
        const clickable = !!(item.computerId || item.orderId || focusFromKind(item.kind))
        return (
          <div key={item.id} className={`${kindClass(item.kind)}${clickable ? ' is-clickable' : ''}`} role="alert">
            <button
              type="button"
              className="staff-toast-body"
              disabled={!clickable}
              onClick={() => openToast(item)}
              title={clickable ? 'Открыть на зале' : undefined}
            >
              <strong>{item.title}</strong>
              <span>{item.body}</span>
              {clickable && <em className="staff-toast-hint">Открыть на зале</em>}
            </button>
            <button type="button" className="staff-toast-close" onClick={() => dismiss(item.id)} aria-label="Закрыть">
              ×
            </button>
          </div>
        )
      })}
    </div>
  )
}
