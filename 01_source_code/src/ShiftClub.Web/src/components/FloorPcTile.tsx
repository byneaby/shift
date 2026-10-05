import type {
  CSSProperties,
  DragEvent as ReactDragEvent,
  MouseEvent as ReactMouseEvent,
  ReactNode,
} from 'react'
import type { OccupancyKind, PcDisplay } from '../occupancy'
import { formatClockFromIso, formatUntilClock } from '../occupancy'

/** Short label for dense tiles: «ПК-8» → «8», «VIP 12» → «12». */
export function shortPcLabel(name: string): string {
  const m = name.match(/(\d+)\s*$/)
  if (m) return m[1]
  return name.length > 6 ? name.slice(0, 5) + '…' : name
}

/** Bottom line on tiles: «до 16:00» for sessions, booking range otherwise. */
export function tileBottomText(
  view: PcDisplay,
  opts?: { remainingSeconds?: number | null; bookingEndsAt?: string | null; bookingStartsAt?: string | null },
): string | null {
  if (view.kind === 'Busy' && opts?.remainingSeconds != null) {
    const timer = formatUntilClock(opts.remainingSeconds) ?? view.timeText
    if (opts.bookingStartsAt) {
      return `${timer} · 📅${formatClockFromIso(opts.bookingStartsAt)}`
    }
    return timer
  }
  if (view.timeText) return view.timeText
  if (opts?.bookingEndsAt) return `до ${formatClockFromIso(opts.bookingEndsAt)}`
  return null
}

export function FloorPcStatusIcon({
  kind,
  offline,
  paused,
}: {
  kind: OccupancyKind
  offline?: boolean
  paused?: boolean
}) {
  if (paused) {
    return (
      <svg className="floor-pc-ico" viewBox="0 0 24 24" aria-hidden>
        <rect x="6" y="5" width="4" height="14" rx="1" fill="currentColor" />
        <rect x="14" y="5" width="4" height="14" rx="1" fill="currentColor" />
      </svg>
    )
  }
  if (offline || kind === 'Offline') {
    return (
      <svg className="floor-pc-ico" viewBox="0 0 24 24" aria-hidden>
        <path
          fill="currentColor"
          d="M12 3a9 9 0 1 0 9 9h-2a7 7 0 1 1-7-7V3zm1 4v5.6l3.2 1.9-.9 1.5L11 13.4V7h2z"
        />
        <path fill="currentColor" d="M3.3 4.7 4.7 3.3l16 16-1.4 1.4z" opacity=".9" />
      </svg>
    )
  }
  if (kind === 'Maintenance' || kind === 'Updating' || kind === 'Setup') {
    return (
      <svg className="floor-pc-ico" viewBox="0 0 24 24" aria-hidden>
        <path
          fill="currentColor"
          d="M22.7 19.3 13.6 10.2a6 6 0 0 0-7.8-7.8l3.1 3.1-2.1 2.1-3.1-3.1a6 6 0 0 0 7.8 7.8l9.1 9.1a1 1 0 0 0 1.4-1.4z"
        />
      </svg>
    )
  }
  if (kind === 'Reserved') {
    return (
      <svg className="floor-pc-ico" viewBox="0 0 24 24" aria-hidden>
        <path
          fill="currentColor"
          d="M7 2h2v2h6V2h2v2h3v18H4V4h3V2zm13 8H4v10h16V10zM6 6v2h12V6H6z"
        />
      </svg>
    )
  }
  if (kind === 'Busy') {
    return (
      <svg className="floor-pc-ico" viewBox="0 0 24 24" aria-hidden>
        <path fill="currentColor" d="M8 5v14l11-7z" />
      </svg>
    )
  }
  // Free / powered on
  return (
    <svg className="floor-pc-ico" viewBox="0 0 24 24" aria-hidden>
      <path
        fill="currentColor"
        d="M12 3a1 1 0 0 1 1 1v7a1 1 0 1 1-2 0V4a1 1 0 0 1 1-1zm4.95 2.64a1 1 0 0 1 1.41.13A8 8 0 1 1 5.64 5.77a1 1 0 1 1 1.54 1.27 6 6 0 1 0 8.5-.14 1 1 0 0 1 .13-1.41z"
      />
    </svg>
  )
}

export type FloorPcTileFaceProps = {
  name: string
  view: PcDisplay
  bottomText?: string | null
  selected?: boolean
  className?: string
  style?: CSSProperties
  title?: string
  onClick?: (e: ReactMouseEvent<HTMLButtonElement>) => void
  onMouseEnter?: (e: ReactMouseEvent<HTMLButtonElement>) => void
  onMouseMove?: (e: ReactMouseEvent<HTMLButtonElement>) => void
  onMouseLeave?: (e: ReactMouseEvent<HTMLButtonElement>) => void
  onDragOver?: (e: ReactDragEvent<HTMLButtonElement>) => void
  onDrop?: (e: ReactDragEvent<HTMLButtonElement>) => void
  children?: ReactNode
  draggable?: boolean
  onDragStart?: (e: ReactDragEvent<HTMLButtonElement>) => void
  /** Small label e.g. PS5 */
  badge?: string | null
}

export function FloorPcTileFace({
  name,
  view,
  bottomText,
  selected,
  className,
  style,
  title,
  onClick,
  onMouseEnter,
  onMouseMove,
  onMouseLeave,
  onDragOver,
  onDrop,
  draggable,
  onDragStart,
  badge,
  children,
}: FloorPcTileFaceProps) {
  const bottom = bottomText ?? view.timeText
  const toneClass = view.timeTone ? ` floor-pc-tile-time--${view.timeTone}` : ''
  return (
    <button
      type="button"
      draggable={draggable}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDrop={onDrop}
      className={`floor-pc-tile floor-pc-tile--${view.kind.toLowerCase()}${view.isPaused ? ' floor-pc-tile--paused' : ''}${
        view.offlineBadge ? ' floor-pc-tile--offline' : ''
      }${badge ? ' floor-pc-tile--has-badge' : ''}${selected ? ' is-selected' : ''}${className ? ` ${className}` : ''}`}
      style={{ ['--pc-accent' as string]: view.color, ...style }}
      title={title}
      onClick={onClick}
      onMouseEnter={onMouseEnter}
      onMouseMove={onMouseMove}
      onMouseLeave={onMouseLeave}
    >
      {badge ? <span className="floor-pc-tile-badge">{badge}</span> : null}
      <span className="floor-pc-tile-top">
        <strong className="floor-pc-tile-num">{shortPcLabel(name)}</strong>
        <span className="floor-pc-tile-ico" style={{ color: view.color }} aria-hidden>
          <FloorPcStatusIcon kind={view.kind} offline={view.offlineBadge} paused={view.isPaused} />
        </span>
      </span>
      {bottom ? (
        <span className={`floor-pc-tile-time${toneClass}`}>{bottom}</span>
      ) : (
        <span className="floor-pc-tile-time floor-pc-tile-time--muted">{view.statusLabel}</span>
      )}
      {children}
    </button>
  )
}

export type FloorHoverInfo = {
  name: string
  zoneName?: string
  view: PcDisplay
  guestName?: string | null
  guestPhone?: string | null
  remainingLabel?: string | null
  untilLabel?: string | null
  bookingHint?: string | null
  x: number
  y: number
}

export function FloorPcHoverCard({ info }: { info: FloorHoverInfo | null }) {
  if (!info) return null
  return (
    <div
      className="floor-pc-hover"
      style={{ left: info.x, top: info.y }}
      role="tooltip"
    >
      <div className="floor-pc-hover-head">
        <span className="floor-pc-hover-status" style={{ color: info.view.color }}>
          {info.view.statusLabel}
        </span>
        {info.remainingLabel && <strong className="floor-pc-hover-timer">{info.remainingLabel}</strong>}
      </div>
      {info.untilLabel && <p className="floor-pc-hover-range">{info.untilLabel}</p>}
      {(info.guestName || info.guestPhone) && (
        <p className="floor-pc-hover-guest">
          <strong>{info.guestName ?? 'Гость'}</strong>
          {info.guestPhone ? <span className="muted"> · {info.guestPhone}</span> : null}
        </p>
      )}
      {info.bookingHint && <p className="floor-pc-hover-book muted">{info.bookingHint}</p>}
      <p className="floor-pc-hover-loc muted">
        {[info.zoneName, info.name].filter(Boolean).join(' · ')}
      </p>
    </div>
  )
}
