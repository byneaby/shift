import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { money, paymentMethodLabel, receiptStatusLabel, isCashShiftOpen } from '../format'
import { BarPaymentPick, type BarPayMethod } from './BarPaymentPick'

export type BarCartItem = {
  productId: string
  name: string
  price: number
  qty: number
  imageUrl?: string | null
}

export type BarReceiptDto = {
  id: string
  number: string
  status?: string | number
  total: number
  paidTotal: number
  createdAt: string
  items: { name: string; itemType?: string | number; lineTotal: number; quantity: number; unitPrice: number }[]
  payments: { method: string | number; amount: number }[]
}

type CashShiftDto = {
  id: string
  number: string
  status: string | number
  revenueNow: number
}

function isProductReceiptItem(itemType: string | number | undefined) {
  if (itemType === 'Product' || itemType === 2) return true
  return false
}

function receiptHasBarItems(receipt: BarReceiptDto) {
  return receipt.items.some((i) => isProductReceiptItem(i.itemType))
}

type BarPosPanelProps = {
  cart: BarCartItem[]
  cartTotal: number
  paymentMethod: BarPayMethod | null
  onPaymentChange: (method: BarPayMethod) => void
  mixedCash?: string
  onMixedCashChange?: (value: string) => void
  onQtyChange: (productId: string, delta: number) => void
  onClear: () => void
  onPay: () => void
  paying: boolean
  kaspiWaiting?: boolean
  shift: CashShiftDto | null | undefined
  shiftLoading: boolean
  lastReceipt: BarReceiptDto | null
  onDismissReceipt: () => void
  recentReceipts: BarReceiptDto[]
}

export function BarPosPanel({
  cart,
  cartTotal,
  paymentMethod,
  onPaymentChange,
  mixedCash = '',
  onMixedCashChange,
  onQtyChange,
  onClear,
  onPay,
  paying,
  kaspiWaiting = false,
  shift,
  shiftLoading,
  lastReceipt,
  onDismissReceipt,
  recentReceipts,
}: BarPosPanelProps) {
  const barSales = useMemo(
    () => recentReceipts.filter(receiptHasBarItems).slice(0, 12),
    [recentReceipts],
  )

  const shiftOpen = shift != null && isCashShiftOpen(shift.status)
  const canPay = cart.length > 0 && paymentMethod && shiftOpen && !paying

  return (
    <aside className="cash-card bar-pos-cart">
      <div className="bar-pos-shift">
        {shiftLoading ? (
          <span className="muted">Касса…</span>
        ) : shiftOpen ? (
          <>
            <span className="bar-pos-shift__badge is-open">Смена открыта</span>
            <span className="muted">№ {shift!.number}</span>
            <span className="bar-pos-shift__rev">Выручка: {money(shift!.revenueNow)} ₸</span>
          </>
        ) : (
          <>
            <span className="bar-pos-shift__badge is-closed">Смена закрыта</span>
            <Link to="/cash" className="bar-pos-shift__link">
              Открыть кассу →
            </Link>
          </>
        )}
      </div>

      <h2 className="section-title bar-pos-cart__title">Чек</h2>

      <ul className="bar-pos-receipt-lines">
        {cart.map((c) => (
          <li key={c.productId} className="bar-pos-receipt-line">
            <div className="bar-pos-receipt-line__main">
              <strong>{c.name}</strong>
              <span className="muted">
                {money(c.price)} ₸ × {c.qty}
              </span>
            </div>
            <div className="bar-pos-receipt-line__actions">
              <button type="button" className="ghost" onClick={() => onQtyChange(c.productId, -1)} aria-label="Меньше">
                −
              </button>
              <span>{c.qty}</span>
              <button type="button" className="ghost" onClick={() => onQtyChange(c.productId, 1)} aria-label="Больше">
                +
              </button>
            </div>
            <strong className="bar-pos-receipt-line__sum">{money(c.price * c.qty)} ₸</strong>
          </li>
        ))}
        {cart.length === 0 && (
          <li className="bar-pos-receipt-empty muted">Добавьте товары слева — они появятся здесь</li>
        )}
      </ul>

      <div className="bar-pos-total-row">
        <span>К оплате</span>
        <strong>{money(cartTotal)} ₸</strong>
      </div>

      <BarPaymentPick value={paymentMethod} onChange={onPaymentChange} disabled={paying || !shiftOpen} />

      {paymentMethod === 'Mixed' && cartTotal > 0 && onMixedCashChange && (
        <div className="field-grid" style={{ marginTop: 8 }}>
          <label>
            Наличные, ₸
            <input
              type="number"
              min={1}
              step={50}
              value={mixedCash}
              onChange={(e) => onMixedCashChange(e.target.value)}
              placeholder={String(Math.round(cartTotal / 2))}
              disabled={paying}
            />
          </label>
          <div className="start-summary-row" style={{ margin: 0, alignContent: 'end' }}>
            <div className="start-summary-total">
              <span className="muted">Kaspi</span>
              <strong>
                {Math.max(0, Math.round(cartTotal - (Number(mixedCash) || 0))).toLocaleString('ru-RU')} ₸
              </strong>
            </div>
          </div>
        </div>
      )}

      <button type="button" className="bar-primary-btn bar-pos-pay-btn" disabled={!canPay} onClick={onPay}>
        {kaspiWaiting
          ? 'Ждём оплату на терминале…'
          : paying
            ? 'Пробиваем чек…'
            : `Оплатить · ${money(cartTotal)} ₸`}
      </button>

      {cart.length > 0 && (
        <button type="button" className="ghost bar-pos-clear-btn" disabled={paying} onClick={onClear}>
          Очистить чек
        </button>
      )}

      {!shiftOpen && !shiftLoading && (
        <p className="error bar-pos-shift-warn">Без открытой смены продажа невозможна. Откройте кассу.</p>
      )}

      {lastReceipt && (
        <section className="bar-pos-last-sale" aria-live="polite">
          <div className="bar-pos-last-sale__head">
            <span className="bar-pos-last-sale__ok">✓ Продано</span>
            <button type="button" className="ghost" onClick={onDismissReceipt}>
              Скрыть
            </button>
          </div>
          <p className="bar-pos-last-sale__num">
            Чек <strong>{lastReceipt.number}</strong>
          </p>
          <p className="muted bar-pos-last-sale__meta">
            {receiptStatusLabel(lastReceipt.status)} ·{' '}
            {paymentMethodLabel(lastReceipt.payments[0]?.method)} ·{' '}
            {new Date(lastReceipt.createdAt).toLocaleTimeString('ru-RU')}
          </p>
          <ul className="bar-pos-last-sale__items">
            {lastReceipt.items.map((i, idx) => (
              <li key={idx}>
                {i.name} × {i.quantity} — {money(i.lineTotal)} ₸
              </li>
            ))}
          </ul>
          <p className="bar-pos-last-sale__total">
            Итого: <strong>{money(lastReceipt.total)} ₸</strong>
          </p>
        </section>
      )}

      {barSales.length > 0 && (
        <section className="bar-pos-recent">
          <h3 className="section-kicker">Продажи за смену</h3>
          <ul className="bar-pos-recent__list">
            {barSales.map((r) => (
              <li key={r.id} className="bar-pos-recent__row">
                <div>
                  <strong>{r.number}</strong>
                  <span className="muted">
                    {new Date(r.createdAt).toLocaleTimeString('ru-RU')} ·{' '}
                    {paymentMethodLabel(r.payments[0]?.method)}
                  </span>
                </div>
                <strong>{money(r.total)} ₸</strong>
              </li>
            ))}
          </ul>
        </section>
      )}
    </aside>
  )
}
