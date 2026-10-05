/** Build a fixed iCafe-style slot matrix for a zone. */

export type GridPc = {
  id: string
  displayName?: string
  windowsName: string
  gridCol?: number | null
  gridRow?: number | null
  gridColSpan?: number | null
  gridRowSpan?: number | null
}

export type GridCell<T extends GridPc> = {
  col: number
  row: number
  /** Top-left of a multi-cell PC; null for empty or covered-by-span cells. */
  pc: T | null
  /** Covered by another PC's span (not top-left). */
  covered?: boolean
}

function spanOf(pc: GridPc): { cs: number; rs: number } {
  return {
    cs: Math.max(1, Math.floor(pc.gridColSpan ?? 1)),
    rs: Math.max(1, Math.floor(pc.gridRowSpan ?? 1)),
  }
}

export function resolveGridSize(
  zoneCols?: number | null,
  zoneRows?: number | null,
  pcCount = 0,
): { cols: number; rows: number } {
  const cols = Math.max(1, Math.min(24, zoneCols && zoneCols > 0 ? zoneCols : Math.max(4, Math.ceil(Math.sqrt(Math.max(pcCount, 1))))))
  let rows = zoneRows && zoneRows > 0 ? zoneRows : Math.max(2, Math.ceil(pcCount / cols) || 2)
  rows = Math.max(1, Math.min(24, rows))
  while (cols * rows < pcCount) rows++
  return { cols, rows }
}

function markSpan(
  taken: Set<string>,
  col: number,
  row: number,
  cs: number,
  rs: number,
  cols: number,
  rows: number,
): boolean {
  if (col < 0 || row < 0 || col + cs > cols || row + rs > rows) return false
  for (let r = row; r < row + rs; r++) {
    for (let c = col; c < col + cs; c++) {
      if (taken.has(`${c},${r}`)) return false
    }
  }
  for (let r = row; r < row + rs; r++) {
    for (let c = col; c < col + cs; c++) taken.add(`${c},${r}`)
  }
  return true
}

/** Place PCs into slots: prefer saved gridCol/gridRow (+span), then pack remaining by name. */
export function buildZoneSlots<T extends GridPc>(
  pcs: T[],
  zoneCols?: number | null,
  zoneRows?: number | null,
): { cols: number; rows: number; cells: GridCell<T>[] } {
  const sorted = [...pcs].sort((a, b) =>
    (a.displayName ?? a.windowsName).localeCompare(b.displayName ?? b.windowsName, 'ru', { numeric: true }),
  )
  const { cols, rows } = resolveGridSize(zoneCols, zoneRows, sorted.length)
  const taken = new Set<string>()
  const byKey = new Map<string, T>()

  for (const pc of sorted) {
    if (pc.gridCol == null || pc.gridRow == null) continue
    const { cs, rs } = spanOf(pc)
    if (!markSpan(taken, pc.gridCol, pc.gridRow, cs, rs, cols, rows)) continue
    byKey.set(`${pc.gridCol},${pc.gridRow}`, pc)
  }

  let cursor = 0
  for (const pc of sorted) {
    if ([...byKey.values()].includes(pc)) continue
    const { cs, rs } = spanOf(pc)
    while (cursor < cols * rows) {
      const col = cursor % cols
      const row = Math.floor(cursor / cols)
      cursor++
      if (!markSpan(taken, col, row, cs, rs, cols, rows)) continue
      byKey.set(`${col},${row}`, pc)
      break
    }
  }

  const cells: GridCell<T>[] = []
  for (let row = 0; row < rows; row++) {
    for (let col = 0; col < cols; col++) {
      const key = `${col},${row}`
      const pc = byKey.get(key) ?? null
      if (pc) {
        cells.push({ col, row, pc })
        continue
      }
      // Covered by another PC span?
      let covered = false
      for (const [k, owner] of byKey) {
        const [oc, or] = k.split(',').map(Number)
        const { cs, rs } = spanOf(owner)
        if (col >= oc && col < oc + cs && row >= or && row < or + rs) {
          covered = true
          break
        }
      }
      cells.push({ col, row, pc: null, covered })
    }
  }
  return { cols, rows, cells }
}

export function zoneKindLabel(kind?: string | null) {
  switch ((kind ?? 'Hall').toLowerCase()) {
    case 'restroom':
      return 'Уборная'
    case 'cafe':
      return 'Кафе / бар'
    case 'other':
      return 'Зона'
    default:
      return 'Зал'
  }
}

export function isAmenityZone(kind?: string | null) {
  const k = (kind ?? 'Hall').toLowerCase()
  return k === 'restroom' || k === 'cafe' || k === 'other'
}
