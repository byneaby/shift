import { useMemo } from 'react'
import {
  formatRemaining,
  formatUntilClock,
  isSessionPaused,
  resolveOccupancy,
  type OccupancyComputer,
} from '../occupancy'

type UpcomingPc = OccupancyComputer & {
  id: string
  displayName?: string
  windowsName: string
  zoneName?: string
  zoneColorHex?: string
  isMaintenance?: boolean
  sessionGuestName?: string
}

type UpcomingItem = {
  pc: UpcomingPc
  secondsUntil: number
  paused: boolean
}

type FloorUpcomingFreePanelProps = {
  computers: UpcomingPc[]
  freeCount: number
  offlineCount?: number
  busyCount: number
  selectedPcId?: string | null
  onSelectPc: (pcId: string) => void
  maxItems?: number
}

function buildUpcomingItems(computers: UpcomingPc[]): UpcomingItem[] {
  const items: UpcomingItem[] = []
  for (const pc of computers) {
    if (!pc.isApproved || pc.isMaintenance) continue
    const occ = resolveOccupancy(pc)
    if (occ.kind !== 'Busy' || !pc.currentSessionId) continue
    const sec = pc.remainingSeconds
    if (sec == null || sec < 0) continue
    items.push({
      pc,
      secondsUntil: Math.max(0, Math.floor(sec)),
      paused: isSessionPaused(pc),
    })
  }
  // Только по остатку времени; имя — для стабильности (выбор ПК не двигает строку).
  return items.sort((a, b) => {
    const byTime = a.secondsUntil - b.secondsUntil
    if (byTime !== 0) return byTime
    const an = a.pc.displayName ?? a.pc.windowsName
    const bn = b.pc.displayName ?? b.pc.windowsName
    return an.localeCompare(bn, 'ru', { numeric: true })
  })
}

export function FloorUpcomingFreePanel({
  computers,
  freeCount,
  offlineCount = 0,
  busyCount,
  selectedPcId,
  onSelectPc,
  maxItems = 10,
}: FloorUpcomingFreePanelProps) {
  const upcoming = useMemo(
    () => buildUpcomingItems(computers).slice(0, maxItems),
    [computers, maxItems],
  )
  const next = upcoming[0] ?? null

  const stats = (
    <span className="floor-upcoming-free__stats">
      <span className="floor-upcoming-free__stat is-free" title="Включены и свободны">
        <strong>{freeCount}</strong> своб.
      </span>
      {offlineCount > 0 ? (
        <span className="floor-upcoming-free__stat is-offline" title="Выключены, сеанса нет — можно посадить (WOL)">
          <strong>{offlineCount}</strong> выкл
        </span>
      ) : null}
      <span className="floor-upcoming-free__stat is-busy">
        <strong>{busyCount}</strong> занято
      </span>
    </span>
  )

  if (upcoming.length === 0) {
    return (
      <section className="floor-upcoming-free" aria-label="Скоро свободны">
        <div className="floor-upcoming-free__head">
          <p className="section-kicker" style={{ margin: 0 }}>
            Скоро свободны
          </p>
          {stats}
        </div>
        <p className="muted" style={{ margin: 0, fontSize: 13 }}>
          {busyCount > 0
            ? 'Нет таймера на занятых ПК'
            : offlineCount > 0 && freeCount === 0
              ? 'Все выкл — можно посадить, ПК включится сам'
              : 'Все свободны'}
        </p>
      </section>
    )
  }

  return (
    <section className="floor-upcoming-free" aria-label="Скоро свободны">
      <div className="floor-upcoming-free__head">
        <p className="section-kicker" style={{ margin: 0 }}>
          Скоро свободны
        </p>
        {stats}
      </div>

      {next && (
        <p className="floor-upcoming-free__alert">
          Ближайший: <strong>{next.pc.displayName ?? next.pc.windowsName}</strong>
          {' · '}
          <strong>{formatRemaining(next.secondsUntil) ?? '—'}</strong>
          {formatUntilClock(next.secondsUntil) ? ` (${formatUntilClock(next.secondsUntil)})` : ''}
        </p>
      )}

      <ol className="floor-upcoming-free__list">
        {upcoming.map(({ pc, secondsUntil, paused }) => {
          const name = pc.displayName ?? pc.windowsName
          const until = formatUntilClock(secondsUntil)
          const rem = formatRemaining(secondsUntil)
          const soon = secondsUntil <= 5 * 60
          const selected = selectedPcId === pc.id
          return (
            <li key={pc.id}>
              <button
                type="button"
                className={`floor-upcoming-free__row${soon ? ' is-soon' : ''}${selected ? ' is-selected' : ''}`}
                onClick={() => onSelectPc(pc.id)}
                aria-current={selected ? 'true' : undefined}
              >
                <span className="floor-upcoming-free__row-main">
                  <strong>{name}</strong>
                  {pc.zoneName && <em>{pc.zoneName}</em>}
                </span>
                <span className="floor-upcoming-free__row-time">
                  {paused ? 'пауза · ' : ''}
                  {rem ?? '—'}
                  {until && <small>{until}</small>}
                </span>
              </button>
            </li>
          )
        })}
      </ol>
    </section>
  )
}
