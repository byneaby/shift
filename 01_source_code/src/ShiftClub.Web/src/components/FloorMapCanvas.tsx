import {
  useRef,
  useState,
  type CSSProperties,
  type DragEvent,
  type MouseEvent as ReactMouseEvent,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
} from 'react'
import { createPortal } from 'react-dom'
import {
  buildPcDisplay,
  formatClockFromIso,
  formatRemainingClock,
  formatUntilClock,
  type BookingTimeHint,
} from '../occupancy'
import { FloorPcHoverCard, FloorPcTileFace, tileBottomText, type FloorHoverInfo } from './FloorPcTile'

export type FloorMapPc = {
  id: string
  displayName?: string
  windowsName: string
  gridCol?: number | null
  gridRow?: number | null
  gridColSpan?: number | null
  gridRowSpan?: number | null
  mapX?: number | null
  mapY?: number | null
  isApproved: boolean
  status: string | number
  occupancy?: string
  occupancyDetail?: string | null
  currentSessionId?: string
  remainingSeconds?: number
  sessionStatus?: string | number | null
  sessionGuestName?: string | null
  zoneColorHex?: string
  zoneName?: string
  bookingStartsAt?: string | null
  bookingEndsAt?: string | null
  bookingContactName?: string | null
  bookingContactPhone?: string | null
  stationKind?: string | number
}

export type FloorMapElementKind =
  | 'Cashier'
  | 'Entrance'
  | 'Restroom'
  | 'Bar'
  | 'Label'
  | 'Rect'
  | 'Wall'
  | number

export type FloorMapElement = {
  id: string
  kind: FloorMapElementKind
  label?: string | null
  gridCol: number
  gridRow: number
  colSpan: number
  rowSpan: number
  endCol?: number | null
  endRow?: number | null
  colorHex?: string | null
  fillHex?: string | null
  strokeWidth?: number | null
  fontSize?: number | null
  sortOrder?: number
  showIcon?: boolean
  rotationDeg?: number
  isVisible: boolean
}

export type FloorMapCanvasProps = {
  gridCols: number
  gridRows: number
  computers: FloorMapPc[]
  elements: FloorMapElement[]
  mode?: 'view' | 'edit'
  selectedPcId?: string | null
  selectedPcIds?: readonly string[]
  selectedElementId?: string | null
  onPcClick?: (
    pc: FloorMapPc,
    cell: { col: number; row: number },
    additive: boolean,
  ) => void
  onPcDrop?: (pcId: string, col: number, row: number) => void
  /** Resize PC tile (gridColSpan/gridRowSpan). */
  onPcResize?: (pcId: string, colSpan: number, rowSpan: number) => void
  onCellClick?: (col: number, row: number) => void
  onElementClick?: (el: FloorMapElement) => void
  /** Move element to a new top-left cell (walls keep length via endCol/endRow). */
  onElementMove?: (
    el: FloorMapElement,
    col: number,
    row: number,
    ends?: { endCol: number; endRow: number },
  ) => void
  /** Resize non-wall element (colSpan/rowSpan). */
  onElementResize?: (el: FloorMapElement, colSpan: number, rowSpan: number) => void
  /** Set wall endpoints (start = gridCol/gridRow, end = endCol/endRow). */
  onElementWallEndpoints?: (
    el: FloorMapElement,
    startCol: number,
    startRow: number,
    endCol: number,
    endRow: number,
  ) => void
  /** Inline label edit (double-click). */
  onElementLabelCommit?: (el: FloorMapElement, label: string) => void
  wallDraft?: { col: number; row: number } | null
  className?: string
  hideInvisible?: boolean
  disableHoverCard?: boolean
  /** Branch floor background (#hex). */
  backgroundHex?: string | null
  /** Hide cell grid lines (visitor TV display). */
  hideGrid?: boolean
}

function kindKey(kind: FloorMapElementKind): string {
  if (typeof kind === 'number') {
    return ['Cashier', 'Entrance', 'Restroom', 'Bar', 'Label', 'Rect', 'Wall'][kind] ?? 'Label'
  }
  return kind
}

export function elementKindLabel(kind: FloorMapElementKind): string {
  switch (kindKey(kind)) {
    case 'Cashier':
      return 'Касса'
    case 'Entrance':
      return 'Вход'
    case 'Restroom':
      return 'Уборная'
    case 'Bar':
      return 'Бар'
    case 'Label':
      return 'Подпись'
    case 'Rect':
      return 'Зона'
    case 'Wall':
      return 'Стена'
    default:
      return 'Элемент'
  }
}

export function elementKindIcon(kind: FloorMapElementKind): string {
  switch (kindKey(kind)) {
    case 'Cashier':
      return 'К'
    case 'Entrance':
      return '⇢'
    case 'Restroom':
      return 'WC'
    case 'Bar':
      return 'Б'
    case 'Label':
      return 'Aa'
    case 'Rect':
      return '▢'
    case 'Wall':
      return '━'
    default:
      return '•'
  }
}

export function defaultElementColor(kind: FloorMapElementKind): string {
  switch (kindKey(kind)) {
    case 'Cashier':
      return '#f59e0b'
    case 'Entrance':
      return '#34d399'
    case 'Restroom':
      return '#60a5fa'
    case 'Bar':
      return '#fb7185'
    case 'Label':
      return '#e2e8f0'
    case 'Rect':
      return '#475569'
    case 'Wall':
      return '#94a3b8'
    default:
      return '#94a3b8'
  }
}

/** Assign grid cells: prefer saved GridCol/Row (+span), else mapX/Y, else pack. */
export function assignPcCells(
  pcs: FloorMapPc[],
  gridCols: number,
  gridRows: number,
): Map<string, { col: number; row: number; colSpan: number; rowSpan: number }> {
  const result = new Map<string, { col: number; row: number; colSpan: number; rowSpan: number }>()
  const taken = new Set<string>()
  const sorted = [...pcs].sort((a, b) =>
    (a.displayName ?? a.windowsName).localeCompare(b.displayName ?? b.windowsName, 'ru', { numeric: true }),
  )

  const spanOf = (pc: FloorMapPc) => ({
    cs: Math.max(1, Math.floor(pc.gridColSpan ?? 1)),
    rs: Math.max(1, Math.floor(pc.gridRowSpan ?? 1)),
  })

  const tryPlace = (id: string, col: number, row: number, cs: number, rs: number) => {
    if (col < 0 || row < 0 || col + cs > gridCols || row + rs > gridRows) return false
    for (let r = row; r < row + rs; r++) {
      for (let c = col; c < col + cs; c++) {
        if (taken.has(`${c},${r}`)) return false
      }
    }
    for (let r = row; r < row + rs; r++) {
      for (let c = col; c < col + cs; c++) taken.add(`${c},${r}`)
    }
    result.set(id, { col, row, colSpan: cs, rowSpan: rs })
    return true
  }

  for (const pc of sorted) {
    if (pc.gridCol != null && pc.gridRow != null) {
      const { cs, rs } = spanOf(pc)
      tryPlace(pc.id, pc.gridCol, pc.gridRow, cs, rs)
    }
  }

  for (const pc of sorted) {
    if (result.has(pc.id)) continue
    const { cs, rs } = spanOf(pc)
    if (pc.mapX != null && pc.mapY != null) {
      const col = Math.min(gridCols - cs, Math.max(0, Math.floor(pc.mapX / 100)))
      const row = Math.min(gridRows - rs, Math.max(0, Math.floor(pc.mapY / 100)))
      if (tryPlace(pc.id, col, row, cs, rs)) continue
    }
  }

  let cursor = 0
  for (const pc of sorted) {
    if (result.has(pc.id)) continue
    const { cs, rs } = spanOf(pc)
    while (cursor < gridCols * gridRows) {
      const col = cursor % gridCols
      const row = Math.floor(cursor / gridCols)
      cursor++
      if (tryPlace(pc.id, col, row, cs, rs)) break
    }
  }

  return result
}

function buildHoverInfo(
  pc: FloorMapPc,
  view: ReturnType<typeof buildPcDisplay>,
  e: ReactMouseEvent,
): FloorHoverInfo {
  const name = pc.displayName ?? pc.windowsName
  const until = formatUntilClock(pc.remainingSeconds)
  return {
    name,
    zoneName: pc.zoneName,
    view,
    guestName: pc.sessionGuestName ?? pc.bookingContactName,
    guestPhone: pc.bookingContactPhone,
    remainingLabel: formatRemainingClock(pc.remainingSeconds),
    untilLabel: until,
    bookingHint:
      pc.bookingStartsAt
        ? `Бронь ${formatClockFromIso(pc.bookingStartsAt)}${
            pc.bookingEndsAt ? `–${formatClockFromIso(pc.bookingEndsAt)}` : ''
          }`
        : null,
    x: Math.min(window.innerWidth - 280, e.clientX + 14),
    y: Math.min(window.innerHeight - 160, e.clientY + 14),
  }
}

function strokeSvgWidth(strokeWidth: number | null | undefined): number {
  const n = strokeWidth ?? 4
  return Math.max(0.06, Math.min(0.55, n * 0.045))
}

export function FloorMapCanvas({
  gridCols,
  gridRows,
  computers,
  elements,
  mode = 'view',
  selectedPcId,
  selectedPcIds,
  selectedElementId,
  onPcClick,
  onPcDrop,
  onPcResize,
  onCellClick,
  onElementClick,
  onElementMove,
  onElementResize,
  onElementWallEndpoints,
  onElementLabelCommit,
  wallDraft,
  className,
  hideInvisible = true,
  disableHoverCard = false,
  backgroundHex,
  hideGrid = false,
}: FloorMapCanvasProps) {
  const rootRef = useRef<HTMLDivElement>(null)
  const cells = assignPcCells(computers, gridCols, gridRows)
  const pcById = new Map(computers.map((c) => [c.id, c]))
  const [hover, setHover] = useState<FloorHoverInfo | null>(null)
  const [editingLabelId, setEditingLabelId] = useState<string | null>(null)
  const [labelDraft, setLabelDraft] = useState('')
  type DragMode = 'move' | 'resize' | 'resize-e' | 'resize-s' | 'wall-move' | 'wall-start' | 'wall-end'
  const dragElRef = useRef<{
    id: string
    mode: DragMode
    originCol: number
    originRow: number
    originSpanC: number
    originSpanR: number
    originEndCol: number
    originEndRow: number
  } | null>(null)
  const dragPcRef = useRef<{
    id: string
    mode: 'resize' | 'resize-e' | 'resize-s'
    originCol: number
    originRow: number
    originSpanC: number
    originSpanR: number
  } | null>(null)
  const [pcSpanPreview, setPcSpanPreview] = useState<{
    id: string
    colSpan: number
    rowSpan: number
  } | null>(null)
  const [preview, setPreview] = useState<{
    id: string
    col: number
    row: number
    colSpan: number
    rowSpan: number
    endCol?: number
    endRow?: number
  } | null>(null)

  const visibleElements = [...elements]
    .filter((e) => (hideInvisible ? e.isVisible : true))
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))

  const style = {
    ['--floor-cols' as string]: gridCols,
    ['--floor-rows' as string]: gridRows,
    ...(backgroundHex
      ? {
          background: `
            radial-gradient(ellipse 70% 50% at 50% 35%, rgba(255,255,255,0.06), transparent 70%),
            ${backgroundHex}`,
        }
      : {}),
  } as CSSProperties

  function cellFromClient(clientX: number, clientY: number): { col: number; row: number } | null {
    const root = rootRef.current
    if (!root) return null
    const rect = root.getBoundingClientRect()
    if (rect.width <= 0 || rect.height <= 0) return null
    const col = Math.min(gridCols - 1, Math.max(0, Math.floor(((clientX - rect.left) / rect.width) * gridCols)))
    const row = Math.min(gridRows - 1, Math.max(0, Math.floor(((clientY - rect.top) / rect.height) * gridRows)))
    return { col, row }
  }

  function handleDragStart(e: DragEvent, pcId: string) {
    if (mode !== 'edit') return
    e.dataTransfer.setData('text/pc-id', pcId)
    e.dataTransfer.effectAllowed = 'move'
  }

  function handleCellDrop(e: DragEvent, col: number, row: number) {
    if (mode !== 'edit' || !onPcDrop) return
    e.preventDefault()
    const pcId = e.dataTransfer.getData('text/pc-id')
    if (pcId) onPcDrop(pcId, col, row)
  }

  function beginPcResize(
    e: ReactPointerEvent<Element>,
    pcId: string,
    pos: { col: number; row: number; colSpan: number; rowSpan: number },
    resizeMode: 'resize' | 'resize-e' | 'resize-s',
  ) {
    if (mode !== 'edit') return
    e.preventDefault()
    e.stopPropagation()
    ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
    dragPcRef.current = {
      id: pcId,
      mode: resizeMode,
      originCol: pos.col,
      originRow: pos.row,
      originSpanC: pos.colSpan,
      originSpanR: pos.rowSpan,
    }
    setPcSpanPreview({ id: pcId, colSpan: pos.colSpan, rowSpan: pos.rowSpan })
  }

  function onPcResizeMove(e: ReactPointerEvent<Element>) {
    const drag = dragPcRef.current
    if (!drag) return
    const cell = cellFromClient(e.clientX, e.clientY)
    if (!cell) return
    const colSpan =
      drag.mode === 'resize-s'
        ? drag.originSpanC
        : Math.max(1, Math.min(gridCols - drag.originCol, cell.col - drag.originCol + 1))
    const rowSpan =
      drag.mode === 'resize-e'
        ? drag.originSpanR
        : Math.max(1, Math.min(gridRows - drag.originRow, cell.row - drag.originRow + 1))
    setPcSpanPreview({ id: drag.id, colSpan, rowSpan })
  }

  function onPcResizeUp(e: ReactPointerEvent<Element>) {
    const drag = dragPcRef.current
    const p = pcSpanPreview
    dragPcRef.current = null
    setPcSpanPreview(null)
    if (!drag || !p || p.id !== drag.id) return
    try {
      ;(e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId)
    } catch {
      /* ignore */
    }
    if (p.colSpan !== drag.originSpanC || p.rowSpan !== drag.originSpanR) {
      onPcResize?.(drag.id, p.colSpan, p.rowSpan)
    }
  }

  function beginElementPointer(
    e: ReactPointerEvent<Element>,
    el: FloorMapElement,
    dragMode: DragMode,
  ) {
    if (mode !== 'edit') return
    e.preventDefault()
    e.stopPropagation()
    ;(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId)
    onElementClick?.(el)
    const endCol = el.endCol ?? el.gridCol
    const endRow = el.endRow ?? el.gridRow
    dragElRef.current = {
      id: el.id,
      mode: dragMode,
      originCol: el.gridCol,
      originRow: el.gridRow,
      originSpanC: el.colSpan,
      originSpanR: el.rowSpan,
      originEndCol: endCol,
      originEndRow: endRow,
    }
    setPreview({
      id: el.id,
      col: el.gridCol,
      row: el.gridRow,
      colSpan: el.colSpan,
      rowSpan: el.rowSpan,
      endCol,
      endRow,
    })
  }

  function onElementPointerMove(e: ReactPointerEvent<Element>) {
    const drag = dragElRef.current
    if (!drag) return
    const cell = cellFromClient(e.clientX, e.clientY)
    if (!cell) return

    if (drag.mode === 'move') {
      const maxCol = Math.max(0, gridCols - drag.originSpanC)
      const maxRow = Math.max(0, gridRows - drag.originSpanR)
      setPreview({
        id: drag.id,
        col: Math.min(maxCol, Math.max(0, cell.col)),
        row: Math.min(maxRow, Math.max(0, cell.row)),
        colSpan: drag.originSpanC,
        rowSpan: drag.originSpanR,
      })
      return
    }

    if (drag.mode === 'resize' || drag.mode === 'resize-e' || drag.mode === 'resize-s') {
      const colSpan =
        drag.mode === 'resize-s'
          ? drag.originSpanC
          : Math.max(1, Math.min(gridCols - drag.originCol, cell.col - drag.originCol + 1))
      const rowSpan =
        drag.mode === 'resize-e'
          ? drag.originSpanR
          : Math.max(1, Math.min(gridRows - drag.originRow, cell.row - drag.originRow + 1))
      setPreview({
        id: drag.id,
        col: drag.originCol,
        row: drag.originRow,
        colSpan,
        rowSpan,
      })
      return
    }

    if (drag.mode === 'wall-move') {
      const dCol = cell.col - drag.originCol
      const dRow = cell.row - drag.originRow
      let startCol = drag.originCol + dCol
      let startRow = drag.originRow + dRow
      let endCol = drag.originEndCol + dCol
      let endRow = drag.originEndRow + dRow
      const minC = Math.min(startCol, endCol)
      const maxC = Math.max(startCol, endCol)
      const minR = Math.min(startRow, endRow)
      const maxR = Math.max(startRow, endRow)
      let shiftC = 0
      let shiftR = 0
      if (minC < 0) shiftC = -minC
      if (maxC >= gridCols) shiftC = gridCols - 1 - maxC
      if (minR < 0) shiftR = -minR
      if (maxR >= gridRows) shiftR = gridRows - 1 - maxR
      startCol += shiftC
      endCol += shiftC
      startRow += shiftR
      endRow += shiftR
      setPreview({
        id: drag.id,
        col: startCol,
        row: startRow,
        colSpan: 1,
        rowSpan: 1,
        endCol,
        endRow,
      })
      return
    }

    if (drag.mode === 'wall-start') {
      setPreview({
        id: drag.id,
        col: cell.col,
        row: cell.row,
        colSpan: 1,
        rowSpan: 1,
        endCol: drag.originEndCol,
        endRow: drag.originEndRow,
      })
      return
    }

    if (drag.mode === 'wall-end') {
      setPreview({
        id: drag.id,
        col: drag.originCol,
        row: drag.originRow,
        colSpan: 1,
        rowSpan: 1,
        endCol: cell.col,
        endRow: cell.row,
      })
    }
  }

  function onElementPointerUp(e: ReactPointerEvent<Element>, el: FloorMapElement) {
    const drag = dragElRef.current
    if (!drag || drag.id !== el.id) return
    try {
      ;(e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId)
    } catch {
      /* ignore */
    }
    const p = preview
    dragElRef.current = null
    setPreview(null)
    if (!p) return

    if (drag.mode === 'move') {
      if (p.col !== el.gridCol || p.row !== el.gridRow) onElementMove?.(el, p.col, p.row)
      return
    }
    if (drag.mode === 'resize' || drag.mode === 'resize-e' || drag.mode === 'resize-s') {
      if (p.colSpan !== el.colSpan || p.rowSpan !== el.rowSpan) {
        onElementResize?.(el, p.colSpan, p.rowSpan)
      }
      return
    }
    if (drag.mode === 'wall-move' && p.endCol != null && p.endRow != null) {
      if (
        p.col !== el.gridCol ||
        p.row !== el.gridRow ||
        p.endCol !== el.endCol ||
        p.endRow !== el.endRow
      ) {
        onElementMove?.(el, p.col, p.row, { endCol: p.endCol, endRow: p.endRow })
      }
      return
    }
    if (
      (drag.mode === 'wall-start' || drag.mode === 'wall-end') &&
      p.endCol != null &&
      p.endRow != null
    ) {
      onElementWallEndpoints?.(el, p.col, p.row, p.endCol, p.endRow)
    }
  }

  function commitInlineLabel(el: FloorMapElement) {
    const next = labelDraft.trim()
    setEditingLabelId(null)
    if (next === (el.label ?? '').trim()) return
    onElementLabelCommit?.(el, next.slice(0, 500))
  }

  const gridSlots: ReactNode[] = []
  for (let row = 0; row < gridRows; row++) {
    for (let col = 0; col < gridCols; col++) {
      gridSlots.push(
        <div
          key={`cell-${col}-${row}`}
          className="fmap-cell"
          data-col={col}
          data-row={row}
          onClick={() => onCellClick?.(col, row)}
          onDragOver={(ev) => {
            if (mode === 'edit') ev.preventDefault()
          }}
          onDrop={(ev) => handleCellDrop(ev, col, row)}
        />,
      )
    }
  }

  const wallElements = visibleElements.filter(
    (e) => kindKey(e.kind) === 'Wall' && e.endCol != null && e.endRow != null,
  )
  const decorElements = visibleElements.filter((e) => kindKey(e.kind) !== 'Wall')

  return (
    <div className="fmap-frame">
      <div
        ref={rootRef}
        className={`fmap-root${mode === 'edit' ? ' fmap-root--edit' : ''}${hideGrid ? ' fmap-root--nogrid' : ''}${className ? ` ${className}` : ''}`}
        style={style}
      >
      <div className="fmap-grid" aria-hidden>
        {gridSlots}
      </div>

      <svg className="fmap-walls" viewBox={`0 0 ${gridCols} ${gridRows}`} preserveAspectRatio="none">
        {wallElements.map((e) => {
          const p = preview?.id === e.id ? preview : null
          const x1 = (p?.col ?? e.gridCol) + 0.5
          const y1 = (p?.row ?? e.gridRow) + 0.5
          const x2 = (p?.endCol ?? e.endCol!) + 0.5
          const y2 = (p?.endRow ?? e.endRow!) + 0.5
          const rot = e.rotationDeg ?? 0
          const cx = (x1 + x2) / 2
          const cy = (y1 + y2) / 2
          return (
            <g
              key={e.id}
              transform={rot ? `rotate(${rot} ${cx} ${cy})` : undefined}
              style={{ zIndex: e.sortOrder ?? 0 }}
            >
              <line
                x1={x1}
                y1={y1}
                x2={x2}
                y2={y2}
                className={`fmap-wall${selectedElementId === e.id ? ' is-selected' : ''}${!e.isVisible ? ' is-hidden' : ''}${mode === 'edit' ? ' fmap-wall--editable' : ''}`}
                stroke={e.colorHex ?? defaultElementColor('Wall')}
                strokeWidth={strokeSvgWidth(e.strokeWidth)}
                onClick={(ev) => {
                  ev.stopPropagation()
                  onElementClick?.(e)
                }}
                onPointerDown={(ev) => {
                  beginElementPointer(ev as unknown as ReactPointerEvent<Element>, e, 'wall-move')
                }}
                onPointerMove={(ev) =>
                  onElementPointerMove(ev as unknown as ReactPointerEvent<Element>)
                }
                onPointerUp={(ev) =>
                  onElementPointerUp(ev as unknown as ReactPointerEvent<Element>, e)
                }
                onPointerCancel={() => {
                  dragElRef.current = null
                  setPreview(null)
                }}
              />
              {mode === 'edit' && (
                <>
                  <circle
                    cx={x1}
                    cy={y1}
                    r={0.22}
                    className="fmap-wall-handle"
                    onPointerDown={(ev) =>
                      beginElementPointer(ev as unknown as ReactPointerEvent<Element>, e, 'wall-start')
                    }
                    onPointerMove={(ev) =>
                      onElementPointerMove(ev as unknown as ReactPointerEvent<Element>)
                    }
                    onPointerUp={(ev) =>
                      onElementPointerUp(ev as unknown as ReactPointerEvent<Element>, e)
                    }
                  />
                  <circle
                    cx={x2}
                    cy={y2}
                    r={0.22}
                    className="fmap-wall-handle"
                    onPointerDown={(ev) =>
                      beginElementPointer(ev as unknown as ReactPointerEvent<Element>, e, 'wall-end')
                    }
                    onPointerMove={(ev) =>
                      onElementPointerMove(ev as unknown as ReactPointerEvent<Element>)
                    }
                    onPointerUp={(ev) =>
                      onElementPointerUp(ev as unknown as ReactPointerEvent<Element>, e)
                    }
                  />
                </>
              )}
            </g>
          )
        })}
        {wallDraft && mode === 'edit' && (
          <circle cx={wallDraft.col + 0.5} cy={wallDraft.row + 0.5} r={0.2} className="fmap-wall-draft" />
        )}
      </svg>

      {decorElements.map((e) => {
          const k = kindKey(e.kind)
          const label = e.label || elementKindLabel(e.kind)
          const p = preview?.id === e.id ? preview : null
          const col = p?.col ?? e.gridCol
          const row = p?.row ?? e.gridRow
          const colSpan = p?.colSpan ?? e.colSpan
          const rowSpan = p?.rowSpan ?? e.rowSpan
          const color = e.colorHex ?? defaultElementColor(e.kind)
          const fill = e.fillHex ?? (k === 'Rect' ? color : undefined)
          const showIcon = e.showIcon !== false
          const rot = e.rotationDeg ?? 0
          const z = 2 + (e.sortOrder ?? 0)
          const isEditing = editingLabelId === e.id
          return (
            <div
              key={e.id}
              className={`fmap-el fmap-el--${k.toLowerCase()}${selectedElementId === e.id ? ' is-selected' : ''}${!e.isVisible ? ' is-hidden' : ''}${mode === 'edit' ? ' fmap-el--editable' : ''}${!showIcon ? ' fmap-el--noicon' : ''}`}
              style={
                {
                  gridColumn: `${col + 1} / span ${Math.max(1, colSpan)}`,
                  gridRow: `${row + 1} / span ${Math.max(1, rowSpan)}`,
                  zIndex: z,
                  ['--el-color' as string]: color,
                  ['--el-fill' as string]: fill ?? 'transparent',
                  ['--el-font' as string]: e.fontSize ? `${e.fontSize}px` : undefined,
                  transform: rot ? `rotate(${rot}deg)` : undefined,
                } as CSSProperties
              }
              onClick={(ev: ReactMouseEvent) => {
                ev.stopPropagation()
                onElementClick?.(e)
              }}
              onDoubleClick={(ev) => {
                if (mode !== 'edit' || !onElementLabelCommit) return
                ev.stopPropagation()
                setEditingLabelId(e.id)
                setLabelDraft(e.label ?? '')
              }}
              onPointerDown={(ev) => {
                if (isEditing) return
                beginElementPointer(ev, e, 'move')
              }}
              onPointerMove={onElementPointerMove}
              onPointerUp={(ev) => onElementPointerUp(ev, e)}
              onPointerCancel={() => {
                dragElRef.current = null
                setPreview(null)
              }}
              title={label}
              role="button"
              tabIndex={0}
            >
              {showIcon && <span className="fmap-el-icon">{elementKindIcon(e.kind)}</span>}
              {isEditing ? (
                <textarea
                  className="fmap-el-label-input"
                  value={labelDraft}
                  autoFocus
                  rows={2}
                  maxLength={500}
                  onClick={(ev) => ev.stopPropagation()}
                  onPointerDown={(ev) => ev.stopPropagation()}
                  onChange={(ev) => setLabelDraft(ev.target.value)}
                  onBlur={() => commitInlineLabel(e)}
                  onKeyDown={(ev) => {
                    if (ev.key === 'Escape') {
                      setEditingLabelId(null)
                      return
                    }
                    if (ev.key === 'Enter' && !ev.shiftKey) {
                      ev.preventDefault()
                      commitInlineLabel(e)
                    }
                  }}
                />
              ) : (
                <span className="fmap-el-label">{label}</span>
              )}
              {mode === 'edit' && !isEditing && (
                <>
                  <span
                    className="fmap-el-resize fmap-el-resize--se"
                    onPointerDown={(ev) => beginElementPointer(ev, e, 'resize')}
                    title="Размер"
                  />
                  <span
                    className="fmap-el-resize fmap-el-resize--e"
                    onPointerDown={(ev) => beginElementPointer(ev, e, 'resize-e')}
                    title="Ширина"
                  />
                  <span
                    className="fmap-el-resize fmap-el-resize--s"
                    onPointerDown={(ev) => beginElementPointer(ev, e, 'resize-s')}
                    title="Высота"
                  />
                </>
              )}
            </div>
          )
        })}

      {[...cells.entries()].map(([pcId, pos]) => {
        const pc = pcById.get(pcId)
        if (!pc) return null
        const booking: BookingTimeHint | null =
          pc.bookingStartsAt != null
            ? { startsAt: pc.bookingStartsAt, endsAt: pc.bookingEndsAt ?? undefined }
            : null
        const view = buildPcDisplay(pc, booking)
        const name = pc.displayName ?? pc.windowsName
        const bottom = tileBottomText(view, {
          remainingSeconds: pc.remainingSeconds,
          bookingEndsAt: pc.bookingEndsAt,
          bookingStartsAt: pc.bookingStartsAt,
        })
        const isConsole = pc.stationKind === 'Console' || pc.stationKind === 1
        const previewSpan = pcSpanPreview?.id === pcId ? pcSpanPreview : null
        const colSpan = previewSpan?.colSpan ?? pos.colSpan
        const rowSpan = previewSpan?.rowSpan ?? pos.rowSpan
        return (
          <FloorPcTileFace
            key={pcId}
            name={name}
            view={view}
            bottomText={bottom}
            selected={selectedPcIds?.includes(pcId) ?? selectedPcId === pcId}
            className={`fmap-pc${isConsole ? ' floor-pc-tile--console' : ''}${mode === 'edit' ? ' fmap-pc--editable' : ''}`}
            badge={isConsole ? 'PS5' : null}
            style={
              {
                gridColumn: `${pos.col + 1} / span ${Math.max(1, colSpan)}`,
                gridRow: `${pos.row + 1} / span ${Math.max(1, rowSpan)}`,
                ['--zone-color' as string]: pc.zoneColorHex ?? undefined,
              } as CSSProperties
            }
            draggable={mode === 'edit'}
            onDragStart={(e) => handleDragStart(e, pcId)}
            onDragOver={(e) => {
              if (mode !== 'edit') return
              e.preventDefault()
              e.dataTransfer.dropEffect = 'move'
            }}
            onDrop={(e) => {
              e.preventDefault()
              e.stopPropagation()
              handleCellDrop(e, pos.col, pos.row)
            }}
            onClick={(ev) => {
              ev.stopPropagation()
              onPcClick?.(pc, pos, ev.ctrlKey || ev.metaKey)
            }}
            onMouseEnter={(ev) => {
              if (disableHoverCard || mode === 'edit') return
              setHover(buildHoverInfo(pc, view, ev))
            }}
            onMouseMove={(ev) => {
              if (disableHoverCard || mode === 'edit' || !hover) return
              setHover(buildHoverInfo(pc, view, ev))
            }}
            onMouseLeave={() => setHover(null)}
          >
            {mode === 'edit' && (
              <>
                <span
                  className="fmap-el-resize fmap-el-resize--se"
                  onPointerDown={(ev) => beginPcResize(ev, pcId, { ...pos, colSpan, rowSpan }, 'resize')}
                  onPointerMove={onPcResizeMove}
                  onPointerUp={onPcResizeUp}
                  onPointerCancel={() => {
                    dragPcRef.current = null
                    setPcSpanPreview(null)
                  }}
                  title="Размер ПК"
                />
                <span
                  className="fmap-el-resize fmap-el-resize--e"
                  onPointerDown={(ev) => beginPcResize(ev, pcId, { ...pos, colSpan, rowSpan }, 'resize-e')}
                  onPointerMove={onPcResizeMove}
                  onPointerUp={onPcResizeUp}
                  onPointerCancel={() => {
                    dragPcRef.current = null
                    setPcSpanPreview(null)
                  }}
                  title="Ширина"
                />
                <span
                  className="fmap-el-resize fmap-el-resize--s"
                  onPointerDown={(ev) => beginPcResize(ev, pcId, { ...pos, colSpan, rowSpan }, 'resize-s')}
                  onPointerMove={onPcResizeMove}
                  onPointerUp={onPcResizeUp}
                  onPointerCancel={() => {
                    dragPcRef.current = null
                    setPcSpanPreview(null)
                  }}
                  title="Высота"
                />
              </>
            )}
          </FloorPcTileFace>
        )
      })}

      {!disableHoverCard && createPortal(<FloorPcHoverCard info={hover} />, document.body)}
      </div>
    </div>
  )
}
