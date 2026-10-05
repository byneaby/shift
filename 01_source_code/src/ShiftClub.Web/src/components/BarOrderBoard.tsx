import { useMemo, type ReactNode } from 'react'
import { barOrderPaymentModeLabel, barOrderStatusLabel, isBarPaidOnDelivery, money } from '../format'

export type BarOrderPaidWith = 'Cash' | 'KaspiQr'

export type BarOrderRow = {
  id: string
  number: string
  status: string | number
  paymentMode: string | number
  computerName?: string
  total: number
  createdAt: string
  items: { productName: string; quantity: number; lineTotal: number }[]
}

function orderStatus(status: string | number): string {
  if (typeof status === 'string') return status
  const map = ['New', 'Accepted', 'Preparing', 'Ready', 'Delivering', 'Completed', 'Cancelled', 'Rejected']
  return map[status] ?? String(status)
}

function paymentHint(mode: string | number) {
  const name = barOrderPaymentModeLabel(mode)
  if (isBarPaidOnDelivery(mode)) {
    return name === 'У кассы' ? 'Оплата при выдаче' : `Оплата при выдаче · ${name}`
  }
  if (name === 'С баланса') return 'Уже оплачено · баланс'
  if (name === 'К сеансу') return 'Списать с баланса сеанса'
  return name
}

function preferredPaidWith(mode: string | number): BarOrderPaidWith {
  return barOrderPaymentModeLabel(mode) === 'Kaspi QR' ? 'KaspiQr' : 'Cash'
}

type BarOrderBoardProps = {
  orders: BarOrderRow[]
  busyId: string | null
  onStatus: (id: string, status: string, paidWith?: BarOrderPaidWith) => void
}

export function BarOrderBoard({ orders, busyId, onStatus }: BarOrderBoardProps) {
  const columns = useMemo(() => {
    const col = { new: [] as BarOrderRow[], work: [] as BarOrderRow[], ready: [] as BarOrderRow[] }
    for (const o of orders) {
      const st = orderStatus(o.status)
      if (st === 'New') col.new.push(o)
      else if (st === 'Accepted' || st === 'Preparing') col.work.push(o)
      else if (st === 'Ready' || st === 'Delivering') col.ready.push(o)
    }
    return col
  }, [orders])

  return (
    <section className="cash-card bar-order-kanban">
      <div className="bar-order-kanban__head">
        <h2 className="section-title" style={{ margin: 0 }}>
          Заказы с ПК
        </h2>
        <p className="muted" style={{ margin: 0, fontSize: 13 }}>
          Клиент заказал с компьютера — соберите и отметьте выдачу
        </p>
      </div>

      <div className="bar-order-kanban__cols">
        <OrderColumn
          title="Новые"
          tone="new"
          orders={columns.new}
          busyId={busyId}
          onStatus={onStatus}
          renderActions={(o) => (
            <button type="button" disabled={busyId === o.id} onClick={() => onStatus(o.id, 'Accepted')}>
              Принять в работу
            </button>
          )}
        />
        <OrderColumn
          title="Готовим"
          tone="work"
          orders={columns.work}
          busyId={busyId}
          onStatus={onStatus}
          renderActions={(o) => (
            <button type="button" disabled={busyId === o.id} onClick={() => onStatus(o.id, 'Ready')}>
              Готово к выдаче
            </button>
          )}
        />
        <OrderColumn
          title="Готовы"
          tone="ready"
          orders={columns.ready}
          busyId={busyId}
          onStatus={onStatus}
          renderActions={(o) => (
            <>
              {isBarPaidOnDelivery(o.paymentMode) ? (
                (['Cash', 'KaspiQr'] as const).map((m) => (
                  <button
                    key={m}
                    type="button"
                    className={preferredPaidWith(o.paymentMode) === m ? undefined : 'ghost'}
                    disabled={busyId === o.id}
                    onClick={() => onStatus(o.id, 'Completed', m)}
                  >
                    {m === 'Cash' ? 'Наличные · выдан' : 'Kaspi QR · выдан'}
                  </button>
                ))
              ) : (
                <button type="button" disabled={busyId === o.id} onClick={() => onStatus(o.id, 'Completed')}>
                  Выдан
                </button>
              )}
              <button
                type="button"
                className="ghost"
                disabled={busyId === o.id}
                onClick={() => onStatus(o.id, 'Cancelled')}
              >
                Отмена
              </button>
            </>
          )}
        />
      </div>

      {orders.length === 0 && (
        <p className="muted bar-order-kanban__empty">Нет открытых заказов — новые появятся с ПК клиентов</p>
      )}
    </section>
  )
}

function OrderColumn({
  title,
  tone,
  orders,
  busyId,
  onStatus,
  renderActions,
}: {
  title: string
  tone: 'new' | 'work' | 'ready'
  orders: BarOrderRow[]
  busyId: string | null
  onStatus: (id: string, status: string) => void
  renderActions: (order: BarOrderRow) => ReactNode
}) {
  return (
    <div className={`bar-order-col bar-order-col--${tone}`}>
      <div className="bar-order-col__head">
        <strong>{title}</strong>
        <span className="bar-order-col__count">{orders.length}</span>
      </div>
      <ul className="bar-order-col__list">
        {orders.map((o) => {
          const fromPc = Boolean(o.computerName)
          return (
            <li key={o.id} className="bar-order-card">
              <div className="bar-order-card__top">
                {fromPc ? (
                  <strong className="bar-order-pc">{o.computerName}</strong>
                ) : (
                  <strong>{o.number}</strong>
                )}
                <span className={`chip bar-status-chip bar-order-card__status`}>
                  {barOrderStatusLabel(o.status)}
                </span>
              </div>
              {fromPc && <span className="muted">№ {o.number}</span>}
              <p className={`bar-order-card__pay${isBarPaidOnDelivery(o.paymentMode) ? ' is-cashier' : ''}`}>
                {paymentHint(o.paymentMode)}
              </p>
              <ul className="bar-order-card__items">
                {o.items.map((i, idx) => (
                  <li key={idx}>
                    <span>{i.productName}</span>
                    <span>× {i.quantity}</span>
                  </li>
                ))}
              </ul>
              <div className="bar-order-card__foot">
                <strong>{money(o.total)} ₸</strong>
                <span className="muted">{new Date(o.createdAt).toLocaleTimeString('ru-RU')}</span>
              </div>
              <div className="bar-order-card__actions order-actions">{renderActions(o)}</div>
              {orderStatus(o.status) === 'New' && (
                <button
                  type="button"
                  className="ghost bar-order-card__cancel"
                  disabled={busyId === o.id}
                  onClick={() => onStatus(o.id, 'Cancelled')}
                >
                  Отменить
                </button>
              )}
            </li>
          )
        })}
        {orders.length === 0 && <li className="muted bar-order-col__empty">—</li>}
      </ul>
    </div>
  )
}
