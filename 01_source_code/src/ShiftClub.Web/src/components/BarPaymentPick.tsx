export type BarPayMethod = 'Cash' | 'KaspiQr' | 'Mixed'

type MethodDef = {
  id: BarPayMethod
  label: string
  hint?: string
}

const METHODS: MethodDef[] = [
  { id: 'Cash', label: 'Наличные' },
  { id: 'KaspiQr', label: 'Kaspi QR' },
  { id: 'Mixed', label: 'Смешанная', hint: 'нал + Kaspi' },
]

function PayIcon({ method }: { method: BarPayMethod }) {
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
  }
}

type BarPaymentPickProps = {
  value: BarPayMethod | null
  onChange: (method: BarPayMethod) => void
  disabled?: boolean
}

export function BarPaymentPick({ value, onChange, disabled = false }: BarPaymentPickProps) {
  return (
    <div className="floor-payment-pick">
      <p className="section-kicker" style={{ marginBottom: 8 }}>
        Оплата *
      </p>
      <div className="floor-pay-grid floor-pay-grid--3">
        {METHODS.map((m) => (
          <button
            key={m.id}
            type="button"
            className={`floor-pay-btn${value === m.id ? ' is-active' : ''}${m.id === 'KaspiQr' ? ' is-kaspi' : ''}`}
            disabled={disabled}
            title={m.hint ? `${m.label} · ${m.hint}` : m.label}
            onClick={() => onChange(m.id)}
          >
            <PayIcon method={m.id} />
            <span className="floor-pay-btn__label">{m.label}</span>
            {m.hint && <span className="floor-pay-btn__hint">{m.hint}</span>}
          </button>
        ))}
      </div>
      {!value && !disabled && (
        <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
          Выберите способ оплаты
        </p>
      )}
    </div>
  )
}
