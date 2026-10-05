/** Guest-facing occupancy for PC club maps. Connectivity (offline) is a detail, not exclusive of Free/Busy. */

export type OccupancyKind =
  | 'Free'
  | 'Busy'
  | 'Reserved'
  | 'Offline'
  | 'Maintenance'
  | 'Updating'
  | 'Setup'

export type PcOccupancy = {
  kind: OccupancyKind
  label: string
  detail?: string
  color: string
}

const COLORS: Record<OccupancyKind, string> = {
  Free: '#34d399',
  Busy: '#ef4444',
  Reserved: '#a78bfa',
  Offline: '#64748b',
  Maintenance: '#f59e0b',
  Updating: '#94a3b8',
  Setup: '#fbbf24',
}

const LABELS: Record<OccupancyKind, string> = {
  Free: 'Свободен',
  Busy: 'Занят',
  Reserved: 'Бронь',
  Offline: 'Офлайн',
  Maintenance: 'Техработы',
  Updating: 'Обновление',
  Setup: 'Настройка',
}

export type OccupancyComputer = {
  isApproved: boolean
  status: string | number
  occupancy?: string
  occupancyDetail?: string | null
  currentSessionId?: string
  remainingSeconds?: number
  sessionStatus?: string | number | null
}

export type BookingTimeHint = {
  startsAt: string
  endsAt?: string
}

export type PcTimeTone = 'busy' | 'busy-warn' | 'booking' | 'pause'

/** What cashiers see on a PC tile / status strip. */
export type PcDisplay = {
  kind: OccupancyKind
  color: string
  /** Chip text: Свободен / Выкл / Занят / Пауза / Бронь / … */
  statusLabel: string
  /** Free, no Shell — посадить можно (WOL); в счётчиках это «Выкл», не «Свободно». */
  offlineBadge: boolean
  timeText: string | null
  timeTone: PcTimeTone | null
  isPaused: boolean
  title: string
}

export function resolveOccupancy(pc: OccupancyComputer): PcOccupancy {
  const kind = normalizeKind((pc.occupancy as OccupancyKind) || inferKind(pc), pc)
  const detail = pc.occupancyDetail || undefined
  const offlineFree = kind === 'Free' && (isOfflineDetail(detail) || isOfflineStatus(pc.status))

  return {
    kind,
    label: LABELS[kind] ?? kind,
    detail,
    color: offlineFree ? COLORS.Offline : (COLORS[kind] ?? '#64748b'),
  }
}

export function isSessionPaused(pc: Pick<OccupancyComputer, 'sessionStatus' | 'occupancyDetail'>) {
  const s = pc.sessionStatus
  if (s === 'Paused' || s === 4) return true
  const d = (pc.occupancyDetail ?? '').toLowerCase()
  return d === 'пауза' || d.includes('пауза')
}

export function formatRemaining(seconds?: number | null) {
  if (seconds == null) return null
  const total = Math.max(0, Math.floor(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  if (h > 0) {
    return m > 0 ? `${h} ч ${m} мин` : `${h} ч`
  }
  if (m >= 10) return `${m} мин`
  // последние минуты — с секундами
  return `${m}:${String(s).padStart(2, '0')}`
}

/** Countdown HH:MM:SS for hover cards. */
export function formatRemainingClock(seconds?: number | null) {
  if (seconds == null) return null
  const total = Math.max(0, Math.floor(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

/** Tile footer like «до 16:00» from remaining session time. */
export function formatUntilClock(seconds?: number | null) {
  if (seconds == null) return null
  const end = new Date(Date.now() + Math.max(0, seconds) * 1000)
  return `до ${String(end.getHours()).padStart(2, '0')}:${String(end.getMinutes()).padStart(2, '0')}`
}

export function formatClockFromIso(iso: string) {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

export function formatBookingRange(booking: BookingTimeHint) {
  const from = formatClockFromIso(booking.startsAt)
  if (!booking.endsAt) return `с ${from}`
  return `${from}–${formatClockFromIso(booking.endsAt)}`
}

/**
 * Unified cashier-facing tile content for map + zone grid + side panel strip.
 */
export function buildPcDisplay(
  pc: OccupancyComputer & { displayName?: string; windowsName?: string; zoneName?: string },
  booking?: BookingTimeHint | null,
): PcDisplay {
  const occ = resolveOccupancy(pc)
  const hasOpenBooking = !!booking && !pc.currentSessionId
  // Открытая бронь на свободном ПК — сразу «Бронь» (не ждём soft-hold за 30 мин).
  const kind: OccupancyKind =
    hasOpenBooking && (occ.kind === 'Free' || occ.kind === 'Reserved') ? 'Reserved' : occ.kind
  const offlineBadge =
    kind === 'Free' && (isOfflineDetail(occ.detail) || isOfflineStatus(pc.status) || occ.color === COLORS.Offline)
  const paused = kind === 'Busy' && isSessionPaused(pc)

  let statusLabel = LABELS[kind] ?? kind
  if (paused) statusLabel = 'Пауза'
  else if (kind === 'Free' && offlineBadge) statusLabel = 'Выкл'
  else if (kind === 'Free') statusLabel = 'Свободен'
  else if (kind === 'Reserved') statusLabel = 'Бронь'

  let timeText: string | null = null
  let timeTone: PcTimeTone | null = null

  if (kind === 'Busy' && pc.remainingSeconds != null) {
    timeText = formatRemaining(pc.remainingSeconds)
    if (paused) timeTone = 'pause'
    else if (pc.remainingSeconds < 10 * 60) timeTone = 'busy-warn'
    else timeTone = 'busy'
  } else if (kind === 'Reserved' && hasOpenBooking && booking) {
    timeText = formatBookingRange(booking)
    timeTone = 'booking'
  } else if (kind === 'Reserved' && occ.detail) {
    timeText = occ.detail
    timeTone = 'booking'
  }

  const color = paused ? '#f59e0b' : kind === 'Reserved' ? COLORS.Reserved : offlineBadge ? COLORS.Offline : (COLORS[kind] ?? occ.color)

  const name = pc.displayName ?? pc.windowsName ?? 'ПК'
  const parts = [name, statusLabel]
  if (timeText) parts.push(timeText)
  if (pc.zoneName) parts.push(pc.zoneName)

  return {
    kind,
    color,
    statusLabel,
    offlineBadge,
    timeText,
    timeTone,
    isPaused: paused,
    title: parts.join(' · '),
  }
}

function normalizeKind(kind: OccupancyKind, pc: OccupancyComputer): OccupancyKind {
  if (kind === 'Offline' && !pc.currentSessionId) return 'Free'
  if (kind === 'Offline' && pc.currentSessionId) return 'Busy'
  return kind
}

function isOfflineStatus(status: string | number) {
  const s = String(status)
  return s === 'Offline' || s === 'Error' || s === '0' || s === '10'
}

function isOfflineDetail(detail?: string | null) {
  if (!detail) return false
  const d = detail.toLowerCase()
  return d === 'офлайн' || d === 'offline' || d.includes('офлайн') || d.includes('offline')
}

/** Нет heartbeat / Shell offline, при этом сеанса нет — «выключен», но посадить можно (WOL). */
export function isOfflineFreePc(pc: OccupancyComputer): boolean {
  if (pc.currentSessionId) return false
  const occ = resolveOccupancy(pc)
  if (occ.kind !== 'Free' && occ.kind !== 'Offline') return false
  return (
    isOfflineDetail(occ.detail) ||
    isOfflineStatus(pc.status) ||
    occ.color === COLORS.Offline ||
    occ.kind === 'Offline'
  )
}

/**
 * Кассирская классификация для легенды / счётчиков зала.
 * Выкл ≠ занят: ПК свободен по сеансу, но офлайн.
 */
export type FloorPcBucket = 'onlineFree' | 'offlineFree' | 'busy' | 'reserved' | 'other'

export function classifyFloorPc(
  pc: OccupancyComputer,
  booking?: BookingTimeHint | null,
): FloorPcBucket {
  const view = buildPcDisplay(pc, booking)
  if (view.kind === 'Busy') return 'busy'
  if (view.kind === 'Reserved') return 'reserved'
  if (view.kind === 'Maintenance' || view.kind === 'Updating' || view.kind === 'Setup') return 'other'
  if (view.offlineBadge || isOfflineFreePc(pc)) return 'offlineFree'
  if (view.kind === 'Free') return 'onlineFree'
  return 'other'
}

function inferKind(pc: OccupancyComputer): OccupancyKind {
  const s = String(pc.status)
  if (!pc.isApproved || s === 'PendingApproval' || s === '11') return 'Setup'
  if (s === 'Maintenance' || s === '8') return 'Maintenance'
  if (s === 'Updating' || s === '9') return 'Updating'
  if (s === 'Reserved' || s === '5') return 'Reserved'
  if (
    pc.currentSessionId ||
    s === 'InSession' ||
    s === '4' ||
    s === 'Starting' ||
    s === '6' ||
    s === 'Ending' ||
    s === '7'
  )
    return 'Busy'
  return 'Free'
}
