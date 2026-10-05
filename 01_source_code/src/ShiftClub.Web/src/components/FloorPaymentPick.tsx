export type FloorPayMethod = 'Cash' | 'KaspiQr' | 'Mixed' | 'Balance'

type MethodDef = {
  id: FloorPayMethod
  label: string
  hint?: string
}

const METHODS: MethodDef[] = [
  { id: 'Cash', label: 'Наличные' },
  { id: 'KaspiQr', label: 'Kaspi QR' },
  { id: 'Mixed', label: 'Смешанная' },
  { id: 'Balance', label: 'Баланс' },
]

function PayIcon({ method }: { method: FloorPayMethod }) {
  const common = { width: 28, height: 28, viewBox: '0 0 24 24', fill: 'none', 'aria-hidden': true as const }
  switch (method) {
    case 'Cash':
      return (
        <svg {...common} stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
          <rect x="2" y="6" width="20" height="12" rx="2" />
          <circle cx="12" cy="12" r="2.5" />
          <path d="M6 10h.01M18 14h.01" />
        </svg>
      )
    case 'KaspiQr':
      return (
        <svg {...common} stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
          <rect x="3" y="3" width="7" height="7" rx="1" />
          <rect x="14" y="3" width="7" height="7" rx="1" />
          <rect x="3" y="14" width="7" height="7" rx="1" />
          <path d="M14 14h2v2h-2zM18 14h3v3h-3zM14 18h2v3h-2zM18 21h3v-3" />
        </svg>
      )
    case 'Mixed':
      return (
        <svg {...common} stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
          <path d="M4 7h7v10H4z" />
          <path d="M13 7h7v10h-7z" />
          <path d="M8 12h8" />
        </svg>
      )
    case 'Balance':
      return (
        <svg {...common} stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
          <path d="M4 8h16a2 2 0 0 1 2 2v1H2v-1a2 2 0 0 1 2-2z" />
          <path d="M2 11v5a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-5" />
          <circle cx="17" cy="14" r="1.2" fill="currentColor" stroke="none" />
        </svg>
      )
  }
}

type FloorPaymentPickProps = {
  value: FloorPayMethod | null
  onChange: (method: FloorPayMethod) => void
  disabled?: boolean
  balanceAvailable?: boolean
  balanceHint?: string | null
  required?: boolean
}

export function FloorPaymentPick({
  value,
  onChange,
  disabled = false,
  balanceAvailable = false,
  balanceHint,
  required = true,
}: FloorPaymentPickProps) {
  return (
    <div className="floor-payment-pick">
      <p className="section-kicker" style={{ marginBottom: 8 }}>
        Как платит{required ? ' *' : ''}
      </p>
      <div className="floor-pay-grid">
        {METHODS.map((m) => {
          const isBalance = m.id === 'Balance'
          const btnDisabled = disabled || (isBalance && !balanceAvailable)
          const active = value === m.id
          const hint =
            isBalance && balanceHint
              ? balanceHint
              : isBalance && !balanceAvailable
                ? 'нет клиента'
                : m.id === 'Mixed'
                  ? 'нал + Kaspi'
                  : undefined

          return (
            <button
              key={m.id}
              type="button"
              className={`floor-pay-btn${active ? ' is-active' : ''}${m.id === 'KaspiQr' ? ' is-kaspi' : ''}`}
              disabled={btnDisabled}
              title={hint ? `${m.label} · ${hint}` : m.label}
              onClick={() => onChange(m.id)}
            >
              <PayIcon method={m.id} />
              <span className="floor-pay-btn__label">{m.label}</span>
              {hint && <span className="floor-pay-btn__hint">{hint}</span>}
            </button>
          )
        })}
      </div>
      {required && !value && !disabled && (
        <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
          Выберите способ оплаты
        </p>
      )}
    </div>
  )
}

export type FloorPaymentPart = { method: string; amount: number }

/** Разбить итог на нал + Kaspi. Возвращает null если невалидно. */
export function buildMixedPayments(due: number, cashRaw: string): FloorPaymentPart[] | null {
  const total = Math.round(Number(due) || 0)
  if (total <= 0) return null
  const cash = Math.round(Number(cashRaw) || 0)
  const kaspi = total - cash
  if (cash <= 0 || kaspi <= 0) return null
  return [
    { method: 'Cash', amount: cash },
    { method: 'KaspiQr', amount: kaspi },
  ]
}

/** Разделить части оплаты поровну между N сеансами (остаток — на первые единицы). */
export function dividePaymentParts(parts: FloorPaymentPart[], unitCount: number): FloorPaymentPart[][] {
  if (unitCount <= 1) return [parts]
  const totals: Record<string, number> = {}
  for (const p of parts) totals[p.method] = (totals[p.method] ?? 0) + p.amount

  const splitAmount = (total: number): number[] => {
    if (total <= 0) return Array(unitCount).fill(0)
    const base = Math.floor(total / unitCount)
    let rem = total - base * unitCount
    return Array.from({ length: unitCount }, () => {
      const extra = rem > 0 ? 1 : 0
      if (extra) rem -= 1
      return base + extra
    })
  }

  const cashSplit = splitAmount(totals.Cash ?? 0)
  const kaspiSplit = splitAmount(totals.KaspiQr ?? 0)

  return Array.from({ length: unitCount }, (_, i) => {
    const row: FloorPaymentPart[] = []
    if (cashSplit[i] > 0) row.push({ method: 'Cash', amount: cashSplit[i] })
    if (kaspiSplit[i] > 0) row.push({ method: 'KaspiQr', amount: kaspiSplit[i] })
    return row
  })
}

export function resolveUnitPaymentFromParts(parts: FloorPaymentPart[]): {
  paymentMethod: string
  payments?: FloorPaymentPart[]
} {
  const sum = parts.reduce((a, p) => a + p.amount, 0)
  if (sum <= 0) return { paymentMethod: 'Cash' }
  if (parts.length >= 2) return { paymentMethod: 'Mixed', payments: parts }
  return { paymentMethod: parts[0].method, payments: undefined }
}
