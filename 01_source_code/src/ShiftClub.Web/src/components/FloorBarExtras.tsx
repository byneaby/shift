import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { apiFetch, newIdempotencyKey } from '../api/client'
import { payKaspiTerminalIfReady } from '../kaspiPos'
import { ActionDialog } from './ActionDialog'
import { barOrderPaymentModeLabel, barOrderStatusLabel, isBarPaidOnDelivery } from '../format'
import { can, Perm } from '../permissions'

export type FloorCartItem = {
  productId: string
  name: string
  price: number
  qty: number
}

type ProductDto = {
  id: string
  categoryId: string
  categoryName: string
  name: string
  salePrice: number
  stockQty: number
  isActive: boolean
  isLowStock: boolean
}

export type FloorBarOrderDto = {
  id: string
  number: string
  status: string | number
  paymentMode: string | number
  computerId?: string
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

export function cartTotal(cart: FloorCartItem[]) {
  return cart.reduce((s, i) => s + i.price * i.qty, 0)
}

export function addCartItem(cart: FloorCartItem[], p: ProductDto): FloorCartItem[] {
  const existing = cart.find((x) => x.productId === p.id)
  if (existing) return cart.map((x) => (x.productId === p.id ? { ...x, qty: x.qty + 1 } : x))
  return [...cart, { productId: p.id, name: p.name, price: p.salePrice, qty: 1 }]
}

export function changeCartQty(cart: FloorCartItem[], productId: string, delta: number): FloorCartItem[] {
  return cart
    .map((x) => (x.productId === productId ? { ...x, qty: x.qty + delta } : x))
    .filter((x) => x.qty > 0)
}

/** Compact product chips + cart lines for start-session dialog. */
export function FloorStartBarCart({
  cart,
  onChange,
  groupHint,
}: {
  cart: FloorCartItem[]
  onChange: (next: FloorCartItem[]) => void
  groupHint?: string | null
}) {
  const [q, setQ] = useState('')
  const allowed = can(Perm.BarSell)
  const productsQuery = useQuery({
    queryKey: ['bar-products'],
    queryFn: async () => (await apiFetch<ProductDto[]>('/api/bar/products')).data ?? [],
    enabled: allowed,
  })

  const products = useMemo(() => {
    const list = (productsQuery.data ?? []).filter((p) => p.isActive)
    const needle = q.trim().toLowerCase()
    const filtered = !needle
      ? list
      : list.filter(
          (p) =>
            p.name.toLowerCase().includes(needle) || p.categoryName.toLowerCase().includes(needle),
        )
    // In stock first, then out of stock
    return [...filtered]
      .sort((a, b) => Number(b.stockQty > 0) - Number(a.stockQty > 0) || a.name.localeCompare(b.name, 'ru'))
      .slice(0, 24)
  }, [productsQuery.data, q])

  const total = cartTotal(cart)

  if (!allowed) return null

  return (
    <div className="floor-bar-cart">
      <div className="floor-bar-cart-head">
        <p className="section-kicker" style={{ margin: 0 }}>
          Бар к сеансу (опционально)
        </p>
        {total > 0 && <strong>{total.toLocaleString('ru-RU')} ₸</strong>}
      </div>
      <input
        className="floor-bar-search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        placeholder="Поиск товара…"
      />
      <div className="floor-bar-chips">
        {products.map((p) => {
          const out = p.stockQty <= 0
          return (
            <button
              key={p.id}
              type="button"
              className={`floor-bar-chip${out ? ' is-out' : ''}`}
              disabled={out}
              onClick={() => {
                if (out) return
                const existing = cart.find((x) => x.productId === p.id)
                if (existing && existing.qty >= p.stockQty) return
                onChange(addCartItem(cart, p))
              }}
              title={
                out
                  ? `${p.name}: нет на складе`
                  : `${p.salePrice} ₸ · остаток ${p.stockQty}`
              }
            >
              {p.name}
              <span>
                {p.salePrice.toLocaleString('ru-RU')} ₸
                {out ? ' · нет' : ''}
              </span>
            </button>
          )
        })}
        {products.length === 0 && <span className="muted">Нет товаров</span>}
      </div>
      {cart.length > 0 && (
        <ul className="floor-bar-lines">
          {cart.map((line) => (
            <li key={line.productId}>
              <span>
                {line.name} × {line.qty}
              </span>
              <div className="action-chip-row">
                <button type="button" className="ghost" onClick={() => onChange(changeCartQty(cart, line.productId, -1))}>
                  −
                </button>
                <button
                  type="button"
                  className="ghost"
                  onClick={() => {
                    const p = (productsQuery.data ?? []).find((x) => x.id === line.productId)
                    if (p && line.qty >= p.stockQty) return
                    onChange(changeCartQty(cart, line.productId, 1))
                  }}
                >
                  +
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <p className="muted" style={{ margin: 0, fontSize: 12 }}>
        {groupHint ?? 'Товары продаются сразу при старте (чек в кассе), не как заказ в очередь.'}
      </p>
    </div>
  )
}

export async function placeBarOrderForSession(opts: {
  computerId: string
  gamingSessionId: string
  cart: FloorCartItem[]
}) {
  if (opts.cart.length === 0) return null
  return apiFetch<FloorBarOrderDto>('/api/bar/orders', {
    method: 'POST',
    body: JSON.stringify({
      computerId: opts.computerId,
      gamingSessionId: opts.gamingSessionId,
      paymentMode: 'PayAtCashier',
      comment: 'Стартовый заказ с зала',
      idempotencyKey: newIdempotencyKey(),
      items: opts.cart.map((c) => ({ productId: c.productId, quantity: c.qty })),
    }),
  })
}

/** Immediate bar sale (receipt), not a kitchen/cashier order queue. */
export async function sellBarItemsNow(opts: {
  computerId?: string | null
  gamingSessionId?: string | null
  customerId?: string | null
  cart: FloorCartItem[]
  paymentMethod: 'Cash' | 'KaspiQr' | 'Balance' | 'Free' | 'Mixed'
  payments?: { method: string; amount: number }[] | null
  /** Уже провели Kaspi на терминале (например вместе с сеансом). */
  skipKaspiTerminal?: boolean
  comment?: string
}) {
  if (opts.cart.length === 0) return null
  if (opts.paymentMethod === 'Free')
    throw new Error('Для бара выберите оплату: наличные, Kaspi, смешанную или баланс')
  if (opts.paymentMethod === 'Balance' && !opts.customerId && !opts.gamingSessionId)
    throw new Error('Для бара с баланса нужен клиент')
  if (opts.paymentMethod === 'Mixed' && (!opts.payments || opts.payments.length < 2))
    throw new Error('Смешанная оплата бара: укажите нал + Kaspi')
  const total = opts.cart.reduce((s, c) => s + c.price * c.qty, 0)
  const kaspiPart =
    opts.paymentMethod === 'KaspiQr'
      ? total
      : opts.paymentMethod === 'Mixed'
        ? (opts.payments?.find((p) => p.method === 'KaspiQr')?.amount ?? 0)
        : 0
  if (kaspiPart > 0 && !opts.skipKaspiTerminal) {
    await payKaspiTerminalIfReady(kaspiPart)
  }
  return apiFetch('/api/bar/sell', {
    method: 'POST',
    body: JSON.stringify({
      items: opts.cart.map((c) => ({ productId: c.productId, quantity: c.qty })),
      paymentMethod: opts.paymentMethod,
      payments: opts.paymentMethod === 'Mixed' ? opts.payments : undefined,
      computerId: opts.computerId ?? null,
      gamingSessionId: opts.gamingSessionId ?? null,
      customerId: opts.customerId ?? null,
      comment: opts.comment ?? 'Продажа бара с зала',
      idempotencyKey: newIdempotencyKey(),
    }),
  })
}

/** Open orders inbox + detail / status actions. Quiet for cashier. */
export function FloorOrdersDialog({
  open,
  onClose,
  highlightOrderId,
  computerId,
}: {
  open: boolean
  onClose: () => void
  highlightOrderId?: string | null
  computerId?: string | null
}) {
  const queryClient = useQueryClient()
  const [selectedId, setSelectedId] = useState<string | null>(highlightOrderId ?? null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    setSelectedId(highlightOrderId ?? null)
  }, [open, highlightOrderId])

  const ordersQuery = useQuery({
    queryKey: ['bar-orders'],
    queryFn: async () => (await apiFetch<FloorBarOrderDto[]>('/api/bar/orders')).data ?? [],
    enabled: open && can(Perm.BarOrders),
    refetchInterval: open ? 5000 : false,
  })

  const orders = useMemo(() => {
    const list = ordersQuery.data ?? []
    if (!computerId) return list
    return list.filter((o) => o.computerId === computerId)
  }, [ordersQuery.data, computerId])

  const selected = orders.find((o) => o.id === (selectedId ?? highlightOrderId)) ?? orders[0] ?? null

  const statusMutation = useMutation({
    mutationFn: async (input: string | { status: string; paidWith: 'Cash' | 'KaspiQr' }) => {
      if (!selected) throw new Error('Нет заказа')
      const status = typeof input === 'string' ? input : input.status
      const paidWith = typeof input === 'string' ? undefined : input.paidWith
      if (paidWith === 'KaspiQr' && selected.total > 0) await payKaspiTerminalIfReady(selected.total)
      return apiFetch(`/api/bar/orders/${selected.id}/status`, {
        method: 'POST',
        body: JSON.stringify({ status, paymentMethod: paidWith }),
      })
    },
    onSuccess: async () => {
      setError(null)
      await queryClient.invalidateQueries({ queryKey: ['bar-orders'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка статуса'),
  })

  return (
    <ActionDialog
      open={open}
      onClose={onClose}
      title={computerId ? 'Заказы ПК' : 'Заказы бара'}
      description={computerId ? 'Только этот компьютер' : 'Открытые заказы смены'}
      size="lg"
      footer={
        <button type="button" className="ghost" onClick={onClose}>
          Закрыть
        </button>
      }
    >
      <div className="floor-orders-layout">
        <ul className="floor-orders-list">
          {orders.map((o) => (
            <li key={o.id}>
              <button
                type="button"
                className={`floor-order-item${selected?.id === o.id ? ' is-active' : ''}${
                  o.id === highlightOrderId ? ' is-highlight' : ''
                }`}
                onClick={() => setSelectedId(o.id)}
              >
                <strong>{o.number}</strong>
                <span className="muted">
                  {o.computerName ?? 'Без ПК'} · {barOrderStatusLabel(o.status)}
                </span>
                <em>{o.total.toLocaleString('ru-RU')} ₸</em>
              </button>
            </li>
          ))}
          {orders.length === 0 && <li className="muted">Нет открытых заказов</li>}
        </ul>
        {selected && (
          <div className="floor-order-detail">
            <p style={{ margin: '0 0 8px' }}>
              <strong>{selected.number}</strong>
              <span className="muted">
                {' '}
                · {barOrderStatusLabel(selected.status)} · {barOrderPaymentModeLabel(selected.paymentMode)}
              </span>
            </p>
            {selected.computerName && <p className="muted">{selected.computerName}</p>}
            <ul className="floor-order-lines">
              {selected.items.map((it, i) => (
                <li key={`${selected.id}-${i}`}>
                  {it.productName} × {it.quantity}
                  <span>{it.lineTotal.toLocaleString('ru-RU')} ₸</span>
                </li>
              ))}
            </ul>
            <p>
              <strong>Итого {selected.total.toLocaleString('ru-RU')} ₸</strong>
              {isBarPaidOnDelivery(selected.paymentMode) && (
                <span className="chip" style={{ marginLeft: 8 }}>Оплата при выдаче</span>
              )}
            </p>
            <div className="action-chip-row">
              {['New', 'Accepted'].includes(orderStatus(selected.status)) && (
                <button type="button" onClick={() => statusMutation.mutate('Accepted')} disabled={statusMutation.isPending}>
                  Принять
                </button>
              )}
              {['Accepted', 'Preparing'].includes(orderStatus(selected.status)) && (
                <button type="button" onClick={() => statusMutation.mutate('Ready')} disabled={statusMutation.isPending}>
                  Готов
                </button>
              )}
              {['Ready', 'Delivering'].includes(orderStatus(selected.status)) &&
                (isBarPaidOnDelivery(selected.paymentMode) ? (
                  <>
                    <button
                      type="button"
                      onClick={() => statusMutation.mutate({ status: 'Completed', paidWith: 'Cash' })}
                      disabled={statusMutation.isPending}
                    >
                      Наличные · выдан
                    </button>
                    <button
                      type="button"
                      onClick={() => statusMutation.mutate({ status: 'Completed', paidWith: 'KaspiQr' })}
                      disabled={statusMutation.isPending}
                    >
                      Kaspi QR · выдан
                    </button>
                  </>
                ) : (
                  <button type="button" onClick={() => statusMutation.mutate('Completed')} disabled={statusMutation.isPending}>
                    Выдан
                  </button>
                ))}
              {!['Completed', 'Cancelled', 'Rejected'].includes(orderStatus(selected.status)) && (
                <button
                  type="button"
                  className="ghost danger-text"
                  onClick={() => statusMutation.mutate('Cancelled')}
                  disabled={statusMutation.isPending}
                >
                  Отменить
                </button>
              )}
            </div>
            {error && <p className="error">{error}</p>}
          </div>
        )}
      </div>
    </ActionDialog>
  )
}

/** Quick sell bar to active PC session. */
export function FloorQuickBarDialog({
  open,
  onClose,
  computerId,
  gamingSessionId,
  customerId,
  customerBalance,
  computerName,
  title,
  onSold,
}: {
  open: boolean
  onClose: () => void
  computerId?: string | null
  gamingSessionId?: string | null
  customerId?: string | null
  customerBalance?: number | null
  computerName?: string | null
  title?: string
  onSold?: (message: string) => void
}) {
  const queryClient = useQueryClient()
  const [cart, setCart] = useState<FloorCartItem[]>([])
  const [pay, setPay] = useState<'Cash' | 'KaspiQr' | 'Balance'>('Cash')
  const [tendered, setTendered] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const total = cartTotal(cart)
  const change =
    pay === 'Cash' && tendered !== '' && Number(tendered) > total
      ? Math.round((Number(tendered) - total) * 100) / 100
      : 0
  const cashShort =
    pay === 'Cash' && total > 0 && (tendered === '' || Number(tendered) + 0.001 < total)

  useEffect(() => {
    if (!open) return
    setPay(customerId ? 'Cash' : 'Cash')
    setError(null)
    setFlash(null)
  }, [open, customerId])

  useEffect(() => {
    if (!open || pay !== 'Cash' || total <= 0) {
      if (pay !== 'Cash') setTendered('')
      return
    }
    setTendered(String(Math.round(total)))
  }, [open, pay, total])

  const sellMutation = useMutation({
    mutationFn: async () => {
      if (cart.length === 0) throw new Error('Добавьте товары')
      if (cashShort) throw new Error('Клиент дал меньше суммы чека')
      return sellBarItemsNow({
        computerId: computerId ?? null,
        gamingSessionId: gamingSessionId ?? null,
        customerId: customerId ?? null,
        cart,
        paymentMethod: pay,
        comment: computerName ? `Бар · ${computerName}` : 'Быстрая продажа бара с карты зала',
      })
    },
    onSuccess: async () => {
      const msg =
        pay === 'Cash' && change > 0
          ? `Продано · ${total.toLocaleString('ru-RU')} ₸ · сдача ${change.toLocaleString('ru-RU')} ₸`
          : `Продано · ${total.toLocaleString('ru-RU')} ₸`
      setFlash(msg)
      setError(null)
      setCart([])
      setTendered('')
      onSold?.(msg)
      await queryClient.invalidateQueries({ queryKey: ['bar-products'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка продажи'),
  })

  return (
    <ActionDialog
      open={open}
      onClose={() => {
        if (sellMutation.isPending) return
        setCart([])
        setTendered('')
        setError(null)
        setFlash(null)
        onClose()
      }}
      title={title ?? (computerName ? 'Бар на ПК' : 'Быстрая продажа бара')}
      description={computerName ?? 'Продажа без привязки к компьютеру'}
      size="md"
      footer={
        <>
          <button type="button" className="ghost" onClick={onClose} disabled={sellMutation.isPending}>
            Закрыть
          </button>
          <button
            type="button"
            disabled={cart.length === 0 || sellMutation.isPending || cashShort}
            onClick={() => sellMutation.mutate()}
          >
            Продать · {total.toLocaleString('ru-RU')} ₸
          </button>
        </>
      }
    >
      <div className="form-stack">
        <FloorStartBarCart cart={cart} onChange={setCart} />
        <label>
          Оплата бара
          <select value={pay} onChange={(e) => setPay(e.target.value as typeof pay)}>
            <option value="Cash">Наличные</option>
            <option value="KaspiQr">Kaspi QR</option>
            {customerId && (
              <option value="Balance">
                С баланса{customerBalance != null ? ` (${customerBalance.toLocaleString('ru-RU')} ₸)` : ''}
              </option>
            )}
          </select>
        </label>
        {pay === 'Cash' && total > 0 && (
          <label>
            Дал клиент, ₸
            <input
              type="number"
              min={0}
              step={50}
              value={tendered}
              onChange={(e) => setTendered(e.target.value)}
            />
          </label>
        )}
        {pay === 'Cash' && change > 0 && (
          <p className="flash" style={{ margin: 0 }}>
            Сдача: {change.toLocaleString('ru-RU')} ₸
          </p>
        )}
        {pay === 'Cash' && cashShort && tendered !== '' && (
          <p className="error" style={{ margin: 0, fontSize: 13 }}>
            Не хватает {(total - Number(tendered)).toLocaleString('ru-RU')} ₸
          </p>
        )}
        {flash && <p className="flash">{flash}</p>}
        {error && <p className="error">{error}</p>}
      </div>
    </ActionDialog>
  )
}
