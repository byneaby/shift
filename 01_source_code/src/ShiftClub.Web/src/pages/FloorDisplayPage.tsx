import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, getToken } from '../api/client'
import * as signalR from '@microsoft/signalr'
import { classifyFloorPc } from '../occupancy'
import { FloorMapCanvas, type FloorMapElement } from '../components/FloorMapCanvas'
import { isOpenBookingStatus } from '../format'
import { useBranding } from '../branding'

type FloorMapDto = {
  branchId: string
  gridCols: number
  gridRows: number
  backgroundHex?: string | null
  computers: {
    id: string
    displayName?: string
    windowsName: string
    gridCol?: number
    gridRow?: number
    mapX?: number
    mapY?: number
    zoneColorHex?: string
    zoneName?: string
    status: string | number
    isApproved: boolean
    currentSessionId?: string
    remainingSeconds?: number
    occupancy?: string
    occupancyDetail?: string
  }[]
  elements: FloorMapElement[]
}

type DisplayBooking = {
  id: string
  status: string | number
  startsAt: string
  endsAt: string
  computers: { computerId: string; gamingSessionId?: string | null }[]
}

function todayLocalInput() {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

function tomorrowLocalInput() {
  const d = new Date()
  d.setDate(d.getDate() + 1)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

const LEGEND = [
  { label: 'Свободен', color: '#34d399' },
  { label: 'Выкл', color: '#64748b' },
  { label: 'Занят', color: '#ef4444' },
  { label: 'Бронь', color: '#a78bfa' },
  { label: 'Техработы', color: '#f59e0b' },
] as const

const DISPLAY_THEMES = [
  { id: 'arena', name: 'ARENA', hint: 'Кибер' },
  { id: 'ledger', name: 'LEDGER', hint: 'Ведомость' },
  { id: 'boulevard', name: 'BOULEVARD', hint: 'Тёплый зал' },
  { id: 'neon', name: 'NEON', hint: 'Неон' },
  { id: 'cinema', name: 'CINEMA', hint: 'Кино' },
] as const

type DisplayThemeId = (typeof DISPLAY_THEMES)[number]['id']

const THEME_STORAGE_KEY = 'shiftclub.displayTheme'

function readStoredThemeIndex() {
  try {
    const raw = localStorage.getItem(THEME_STORAGE_KEY)
    const n = Number(raw)
    if (Number.isInteger(n) && n >= 0 && n < DISPLAY_THEMES.length) return n
  } catch {
    /* ignore */
  }
  return 0
}

function formatClock(d: Date) {
  const hh = String(d.getHours()).padStart(2, '0')
  const mm = String(d.getMinutes()).padStart(2, '0')
  return { hh, mm, label: `${hh}:${mm}` }
}

export function FloorDisplayPage() {
  const queryClient = useQueryClient()
  const branding = useBranding()
  const [now, setNow] = useState(() => new Date())
  const [themeIndex, setThemeIndex] = useState(readStoredThemeIndex)
  const [hudVisible, setHudVisible] = useState(false)

  const theme = DISPLAY_THEMES[themeIndex] ?? DISPLAY_THEMES[0]

  const applyThemeIndex = useCallback((index: number) => {
    const next = ((index % DISPLAY_THEMES.length) + DISPLAY_THEMES.length) % DISPLAY_THEMES.length
    setThemeIndex(next)
    try {
      localStorage.setItem(THEME_STORAGE_KEY, String(next))
    } catch {
      /* ignore */
    }
    setHudVisible(true)
  }, [])

  useEffect(() => {
    if (!hudVisible) return
    const t = window.setTimeout(() => setHudVisible(false), 1800)
    return () => window.clearTimeout(t)
  }, [hudVisible, themeIndex])

  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(id)
  }, [])

  useEffect(() => {
    let idle: number | undefined
    const wake = () => {
      document.body.classList.remove('display-idle')
      window.clearTimeout(idle)
      idle = window.setTimeout(() => document.body.classList.add('display-idle'), 4000)
    }
    ;['mousemove', 'keydown', 'touchstart'].forEach((e) => window.addEventListener(e, wake))
    wake()
    return () => {
      ;['mousemove', 'keydown', 'touchstart'].forEach((e) => window.removeEventListener(e, wake))
      window.clearTimeout(idle)
      document.body.classList.remove('display-idle')
    }
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Enter' || e.repeat) return
      e.preventDefault()
      applyThemeIndex(themeIndex + 1)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [applyThemeIndex, themeIndex])

  const clock = useMemo(() => formatClock(now), [now])

  const mapQuery = useQuery({
    queryKey: ['floor-map', 'display'],
    queryFn: async () => (await apiFetch<FloorMapDto>('/api/floor-map')).data!,
    refetchInterval: 4000,
    enabled: !!getToken(),
  })

  const bookingsQuery = useQuery({
    queryKey: ['bookings', 'display', todayLocalInput(), tomorrowLocalInput()],
    queryFn: async () => {
      const today = todayLocalInput()
      const tomorrow = tomorrowLocalInput()
      const [a, b] = await Promise.all([
        apiFetch<DisplayBooking[]>(`/api/bookings?date=${today}`),
        apiFetch<DisplayBooking[]>(`/api/bookings?date=${tomorrow}`),
      ])
      const map = new Map<string, DisplayBooking>()
      for (const item of [...(a.data ?? []), ...(b.data ?? [])]) map.set(item.id, item)
      return [...map.values()]
    },
    refetchInterval: 20000,
    enabled: !!getToken(),
  })

  const bookingsByPc = useMemo(() => {
    const map = new Map<string, DisplayBooking>()
    const now = Date.now()
    for (const b of bookingsQuery.data ?? []) {
      if (!isOpenBookingStatus(b.status)) continue
      if (new Date(b.endsAt).getTime() <= now) continue
      for (const c of b.computers) {
        if (c.gamingSessionId) continue
        const prev = map.get(c.computerId)
        if (!prev || new Date(b.startsAt).getTime() < new Date(prev.startsAt).getTime()) {
          map.set(c.computerId, b)
        }
      }
    }
    return map
  }, [bookingsQuery.data])

  useEffect(() => {
    const token = getToken()
    if (!token) return
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/staff?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()
    const refresh = () => {
      void queryClient.invalidateQueries({ queryKey: ['floor-map'] })
      void queryClient.invalidateQueries({ queryKey: ['bookings'] })
    }
    connection.on('ComputerStatusChanged', refresh)
    connection.on('SessionStarted', refresh)
    connection.on('SessionUpdated', refresh)
    connection.on('SessionEnded', refresh)
    connection.on('BookingChanged', refresh)
    void connection.start()
    return () => {
      void connection.stop()
    }
  }, [queryClient])

  const pcs = useMemo(() => {
    const list = mapQuery.data?.computers ?? []
    return list.map((pc) => {
      const booking = bookingsByPc.get(pc.id)
      if (!booking || pc.currentSessionId) return pc
      return {
        ...pc,
        bookingStartsAt: booking.startsAt,
        bookingEndsAt: booking.endsAt,
      }
    })
  }, [mapQuery.data, bookingsByPc])

  const elements = useMemo(() => mapQuery.data?.elements ?? [], [mapQuery.data])
  const gridCols = mapQuery.data?.gridCols ?? 24
  const gridRows = mapQuery.data?.gridRows ?? 16

  const counts = useMemo(() => {
    let free = 0
    let offline = 0
    let busy = 0
    let reserved = 0
    for (const pc of pcs) {
      const booking =
        'bookingStartsAt' in pc && pc.bookingStartsAt && !pc.currentSessionId
          ? { startsAt: String(pc.bookingStartsAt), endsAt: (pc as { bookingEndsAt?: string }).bookingEndsAt }
          : null
      const bucket = classifyFloorPc(pc, booking)
      if (bucket === 'onlineFree') free++
      else if (bucket === 'offlineFree') offline++
      else if (bucket === 'busy') busy++
      else if (bucket === 'reserved') reserved++
    }
    return { free, offline, busy, reserved, total: pcs.length }
  }, [pcs])

  const dense = pcs.length >= 36
  const themeClass = `display-theme-${theme.id as DisplayThemeId}`

  if (!getToken()) {
    return (
      <div className={`display-shell ${themeClass}`}>
        <div className="display-bg" aria-hidden>
          <div className="display-bg__grid" />
          <div className="display-bg__glow display-bg__glow--a" />
          <div className="display-bg__glow display-bg__glow--b" />
          <div className="display-bg__vignette" />
        </div>
        <p className="display-need-login">
          Войдите в панель и откройте эту страницу на втором мониторе.{' '}
          <Link to="/login">Вход</Link>
        </p>
      </div>
    )
  }

  return (
    <div className={`display-shell${dense ? ' display-shell--dense' : ''} ${themeClass}`}>
      <div
        className="display-theme-hud"
        style={{ opacity: hudVisible ? 1 : 0 }}
        aria-live="polite"
      >
        <b>{theme.name}</b>
        <em>Enter · дизайн</em>
        <span>
          {themeIndex + 1} / {DISPLAY_THEMES.length}
        </span>
      </div>

      <div className="display-bg" aria-hidden>
        <div className="display-bg__grid" />
        <div className="display-bg__glow display-bg__glow--a" />
        <div className="display-bg__glow display-bg__glow--b" />
        <div className="display-bg__glow display-bg__glow--c" />
        <div className="display-bg__scan" />
        <div className="display-bg__vignette" />
      </div>

      <header className="display-head">
        <div className="display-head-brand">
          <div className="display-logo">
            <span className="display-logo__mark">{branding.shortName}</span>
            <span className="display-logo__sub">{branding.clubName}</span>
          </div>
          <h1 className="display-title">Зал · прайс</h1>
        </div>

        <div className="display-stats">
          <div className="display-stat display-stat--free" style={{ ['--i' as string]: 0 }}>
            <strong>{counts.free}</strong>
            <span>свободны</span>
          </div>
          <div className="display-stat display-stat--offline" style={{ ['--i' as string]: 1 }}>
            <strong>{counts.offline}</strong>
            <span>выкл</span>
          </div>
          <div className="display-stat display-stat--busy" style={{ ['--i' as string]: 2 }}>
            <strong>{counts.busy}</strong>
            <span>заняты</span>
          </div>
          <div className="display-stat display-stat--reserved" style={{ ['--i' as string]: 3 }}>
            <strong>{counts.reserved}</strong>
            <span>бронь</span>
          </div>
          <div className="display-stat" style={{ ['--i' as string]: 4 }}>
            <strong>{counts.total}</strong>
            <span>всего</span>
          </div>
        </div>

        <time className="display-clock" dateTime={clock.label} aria-label={clock.label}>
          <span className="display-clock-hh">{clock.hh}</span>
          <span className="display-clock-sep" aria-hidden>
            :
          </span>
          <span className="display-clock-mm">{clock.mm}</span>
        </time>
      </header>

      <div className="display-legend">
        {LEGEND.map((item, i) => (
          <span key={item.label} style={{ ['--i' as string]: i }}>
            <i style={{ background: item.color }} />
            {item.label}
          </span>
        ))}
      </div>

      <div className="display-body display-body--fmap">
        <div className="display-map-col">
          {mapQuery.isError && <p className="error">{(mapQuery.error as Error).message}</p>}
          {mapQuery.isLoading && <p className="muted">Загрузка карты…</p>}
          {mapQuery.data && (
            <div className="display-map-frame">
              <FloorMapCanvas
                className="display-fmap"
                gridCols={gridCols}
                gridRows={gridRows}
                computers={pcs}
                elements={elements}
                mode="view"
                hideInvisible
                hideGrid
                disableHoverCard
                backgroundHex={null}
              />
            </div>
          )}
        </div>
      </div>

      <footer className="display-foot">
        <span className="display-foot__badge">{branding.shortName}</span>
        <div className="display-foot__track">
          <span>Карта зала</span>
          <span>{theme.hint}</span>
          <span>Enter · дизайн</span>
          <span>автообновление</span>
          <span>{clock.label}</span>
          <span>свободны {counts.free}</span>
          <span>выкл {counts.offline}</span>
          <span>заняты {counts.busy}</span>
          <span>бронь {counts.reserved}</span>
          <span>Зал · прайс</span>
          <span>{theme.hint}</span>
          <span>Enter · дизайн</span>
          <span>автообновление</span>
          <span>{clock.label}</span>
          <span>свободны {counts.free}</span>
          <span>выкл {counts.offline}</span>
          <span>заняты {counts.busy}</span>
          <span>бронь {counts.reserved}</span>
        </div>
      </footer>
    </div>
  )
}
