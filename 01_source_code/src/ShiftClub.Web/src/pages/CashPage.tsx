import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { apiFetch } from '../api/client'
import { ActionDialog } from '../components/ActionDialog'
import {
  cashMovementTypeLabel,
  formatReceiptItemName,
  isReceiptRefunded,
  money,
  paymentMethodLabel,
  receiptItemTypeLabel,
  receiptStatusLabel,
} from '../format'

type CashRegisterDto = { id: string; branchId: string; name: string; code: string; isActive: boolean }

type CashShiftDto = {
  id: string
  cashRegisterId: string
  cashRegisterName: string
  number: string
  status: string
  openedAt: string
  openingCash: number
  salesCash: number
  salesCard: number
  salesKaspi: number
  salesTransfer: number
  salesOther: number
  refundsCash: number
  refundsCard: number
  refundsKaspi: number
  refundsTransfer: number
  refundsOther: number
  depositsTotal: number
  cashInTotal: number
  cashOutTotal: number
  expenseTotal: number
  expectedCashNow: number
  collectedNow: number
  revenueNow: number
  closingCashActual?: number
  discrepancy?: number
}

type ReceiptDto = {
  id: string
  number: string
  status?: string | number
  total: number
  paidTotal: number
  createdAt: string
  computerId?: string | null
  computerName?: string | null
  stationKind?: string | number | null
  items: { name: string; itemType?: string | number; lineTotal: number; quantity: number; unitPrice: number }[]
  payments: { method: string | number; amount: number }[]
}

function isConsoleStation(kind: string | number | null | undefined) {
  return kind === 'Console' || kind === 1
}

function itemTypeKey(itemType: string | number | undefined) {
  if (typeof itemType === 'number') {
    const map = ['GamingTime', 'Package', 'Product', 'BalanceTopUp', 'Other', 'BookingDeposit', 'CaseKey']
    return map[itemType] ?? String(itemType)
  }
  return String(itemType ?? '')
}

/** Главный акцент чека: ПК / PS / бар / пополнение / бронь. */
function receiptSubject(r: ReceiptDto): { title: string; subtitle: string; tone: 'pc' | 'console' | 'bar' | 'topup' | 'booking' | 'mixed' } {
  const types = new Set((r.items ?? []).map((i) => itemTypeKey(i.itemType)))
  const itemNames = (r.items ?? []).map((i) => formatReceiptItemName(i.name)).filter(Boolean)
  const itemsLine = itemNames.slice(0, 3).join(', ') + (itemNames.length > 3 ? ` +${itemNames.length - 3}` : '')

  if (r.computerName) {
    const console = isConsoleStation(r.stationKind)
    return {
      title: console ? `PS5 · ${r.computerName}` : r.computerName,
      subtitle: itemsLine || (console ? 'Игровое время консоли' : 'Игровое время'),
      tone: console ? 'console' : 'pc',
    }
  }

  if (types.has('BalanceTopUp') && types.size === 1) {
    return { title: 'Пополнение баланса', subtitle: itemsLine || 'На счёт клиента', tone: 'topup' }
  }
  if (types.has('BookingDeposit') && types.size === 1) {
    return { title: 'Предоплата брони', subtitle: itemsLine || 'Депозит', tone: 'booking' }
  }
  if (types.has('Product') && !types.has('GamingTime') && !types.has('Package')) {
    return { title: 'Бар', subtitle: itemsLine || 'Товары', tone: 'bar' }
  }
  if (types.has('GamingTime') || types.has('Package')) {
    return { title: itemsLine || 'Игровое время', subtitle: 'Сеанс', tone: 'pc' }
  }
  return {
    title: itemsLine || 'Чек',
    subtitle: [...types].map((t) => receiptItemTypeLabel(t)).filter(Boolean).join(' · ') || 'Оплата',
    tone: 'mixed',
  }
}

type ZReportDto = {
  number: string
  cashRegisterName: string
  status: string | number
  openedAt: string
  closedAt?: string
  openingCash: number
  expectedCash: number
  closingCashActual?: number
  closingCashExpected?: number
  discrepancy?: number
  salesCash: number
  salesCard: number
  salesKaspi: number
  salesTransfer: number
  salesOther: number
  refundsCash: number
  refundsCard: number
  refundsKaspi: number
  refundsTransfer: number
  refundsOther: number
  cashInTotal: number
  cashOutTotal: number
  expenseTotal: number
  collectedTotal: number
  revenueTotal: number
  depositsTotal: number
  paidReceiptCount: number
  refundedReceiptCount: number
  byPaymentMethod: { key: string; amount: number; count: number }[]
  byItemType: { key: string; amount: number; count: number }[]
  movements: { type: string; amount: number; category?: string; comment?: string; createdAt: string }[]
}

function discrepancyLabel(delta: number) {
  if (Math.abs(delta) < 0.01) return 'Сошлось'
  if (delta < 0) return `Недостача ${money(Math.abs(delta))} ₸`
  return `Излишек ${money(delta)} ₸`
}

export function CashPage() {
  const queryClient = useQueryClient()
  const [registerId, setRegisterId] = useState('')
  const [openingCash, setOpeningCash] = useState(0)
  const [closingCash, setClosingCash] = useState(0)
  const [discrepancyReason, setDiscrepancyReason] = useState('')
  const [movementAmount, setMovementAmount] = useState(1000)
  const [movementComment, setMovementComment] = useState('')
  const [movementType, setMovementType] = useState<'CashIn' | 'CashOut' | 'Expense'>('CashIn')
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const [movementOpen, setMovementOpen] = useState(false)
  const [closeOpen, setCloseOpen] = useState(false)
  const [zOpen, setZOpen] = useState(false)
  const [zReport, setZReport] = useState<ZReportDto | null>(null)
  const [refundId, setRefundId] = useState<string | null>(null)
  const [refundComment, setRefundComment] = useState('')
  const [selectedReceiptId, setSelectedReceiptId] = useState<string | null>(null)

  const registersQuery = useQuery({
    queryKey: ['cash-registers'],
    queryFn: async () => (await apiFetch<CashRegisterDto[]>('/api/cash/registers')).data ?? [],
  })

  const shiftQuery = useQuery({
    queryKey: ['cash-shift-current'],
    queryFn: async () => {
      try {
        return (await apiFetch<CashShiftDto>('/api/cash/shifts/current')).data ?? null
      } catch {
        return null
      }
    },
    refetchInterval: 8000,
  })

  const receiptsQuery = useQuery({
    queryKey: ['cash-receipts', shiftQuery.data?.id],
    enabled: !!shiftQuery.data?.id,
    queryFn: async () =>
      (await apiFetch<ReceiptDto[]>(`/api/cash/shifts/${shiftQuery.data!.id}/receipts`)).data ?? [],
  })

  useEffect(() => {
    if (!registerId && registersQuery.data?.[0]) {
      setRegisterId(registersQuery.data[0].id)
    }
  }, [registerId, registersQuery.data])

  const openMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/cash/shifts/open', {
        method: 'POST',
        body: JSON.stringify({ cashRegisterId: registerId, openingCash, comment: null }),
      }),
    onSuccess: async () => {
      setError(null)
      setFlash('Смена открыта')
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка открытия'),
  })

  const closeMutation = useMutation({
    mutationFn: async () =>
      apiFetch(`/api/cash/shifts/${shiftQuery.data!.id}/close`, {
        method: 'POST',
        body: JSON.stringify({
          closingCashActual: closingCash,
          discrepancyReason: discrepancyReason.trim() || null,
          comment: null,
        }),
      }),
    onSuccess: async () => {
      setError(null)
      setFlash('Смена закрыта')
      setCloseOpen(false)
      const id = shiftQuery.data?.id
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      if (id) {
        try {
          const z = (await apiFetch<ZReportDto>(`/api/cash/shifts/${id}/z-report`)).data
          if (z) {
            setZReport(z)
            setZOpen(true)
          }
        } catch {
          /* ignore */
        }
      }
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка закрытия'),
  })

  const movementMutation = useMutation({
    mutationFn: async () =>
      apiFetch(`/api/cash/shifts/${shiftQuery.data!.id}/movements`, {
        method: 'POST',
        body: JSON.stringify({
          type: movementType,
          amount: movementAmount,
          category: movementType === 'Expense' ? 'прочее' : null,
          comment: movementComment.trim() || null,
        }),
      }),
    onSuccess: async () => {
      setError(null)
      setFlash('Операция по ящику проведена')
      setMovementOpen(false)
      setMovementComment('')
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка движения'),
  })

  const refundMutation = useMutation({
    mutationFn: async (receiptId: string) =>
      apiFetch(`/api/cash/receipts/${receiptId}/refund`, {
        method: 'POST',
        body: JSON.stringify({ comment: refundComment.trim() || 'Возврат с кассы' }),
      }),
    onSuccess: async () => {
      setError(null)
      setFlash('Возврат выполнен')
      setRefundId(null)
      setRefundComment('')
      setSelectedReceiptId(null)
      await queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
      await queryClient.invalidateQueries({ queryKey: ['customers'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка возврата'),
  })

  async function openXReport() {
    if (!shiftQuery.data?.id) return
    setError(null)
    try {
      const z = (await apiFetch<ZReportDto>(`/api/cash/shifts/${shiftQuery.data.id}/z-report`)).data
      if (z) {
        setZReport(z)
        setZOpen(true)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить промежуточный отчёт')
    }
  }

  const shift = shiftQuery.data
  const closeDelta = shift ? closingCash - shift.expectedCashNow : 0
  const receipts = receiptsQuery.data ?? []
  const refundedCount = receipts.filter((r) => isReceiptRefunded(r.status)).length
  const refundTarget = receipts.find((r) => r.id === refundId)
  const selectedReceipt = receipts.find((r) => r.id === selectedReceiptId) ?? null
  const totalRefunds =
    (shift?.refundsCash ?? 0) +
    (shift?.refundsCard ?? 0) +
    (shift?.refundsKaspi ?? 0) +
    (shift?.refundsTransfer ?? 0) +
    (shift?.refundsOther ?? 0)
  const paidReceipts = receipts.filter((r) => !isReceiptRefunded(r.status))
  const paidTotal = useMemo(
    () => paidReceipts.reduce((sum, r) => sum + Number(r.total || 0), 0),
    [paidReceipts],
  )
  const receiptItemsCount = useMemo(
    () => receipts.reduce((sum, r) => sum + (r.items?.length ?? 0), 0),
    [receipts],
  )
  const refundSummary = refundTarget
    ? [
        refundTarget.items.some((i) => String(i.itemType) === 'Product' || i.itemType === 1)
          ? 'товар вернётся на склад'
          : null,
        refundTarget.items.some((i) => String(i.itemType) === 'BalanceTopUp' || i.itemType === 4)
          ? 'баланс клиента откатится назад'
          : null,
        refundTarget.items.some((i) => String(i.itemType) === 'BookingDeposit' || i.itemType === 5)
          ? 'предоплата брони будет снята'
          : null,
      ]
        .filter(Boolean)
        .join(' · ')
    : ''

  return (
    <div className="cash-page">
      <header className="page-head">
        <div>
          <h1>Касса</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Открытие/закрытие смены, наличные в ящике, чеки и возвраты. Продажи — с карты зала, бара и клиентов.
          </p>
        </div>
      </header>
      {flash && (
        <p className="flash" onAnimationEnd={() => setFlash(null)}>
          {flash}
        </p>
      )}
      {registersQuery.isError && <p className="error">{(registersQuery.error as Error).message}</p>}

      <div className="ops-stats">
        <div className="ops-stat" style={{ ['--stat-accent' as string]: shift ? '#3ddc97' : '#64748b' }}>
          <span>Смена</span>
          <strong>{shift ? 'Открыта' : 'Закрыта'}</strong>
        </div>
        <div className="ops-stat" style={{ ['--stat-accent' as string]: '#ff8a2b' }} title="Ожидаемые наличные в ящике">
          <span>В ящике</span>
          <strong>{shift ? `${money(shift.expectedCashNow)} ₸` : '—'}</strong>
        </div>
        <div
          className="ops-stat"
          style={{ ['--stat-accent' as string]: '#60a5fa' }}
          title="Выручка по чекам без пополнений баланса и предоплат броней"
        >
          <span>Выручка смены</span>
          <strong>{shift ? `${money(shift.revenueNow)} ₸` : '—'}</strong>
        </div>
        <div className="ops-stat" style={{ ['--stat-accent' as string]: '#a78bfa' }}>
          <span>Чеков</span>
          <strong>
            {shift ? receipts.length : '—'}
            {refundedCount > 0 ? ` · возвратов ${refundedCount}` : ''}
          </strong>
        </div>
        <div className="ops-stat" style={{ ['--stat-accent' as string]: '#f59e0b' }}>
          <span>Возвраты</span>
          <strong>{shift ? `${money(totalRefunds)} ₸` : '—'}</strong>
        </div>
      </div>

      <div className="cash-layout">
        <section className="cash-card">
          <h2 className="section-title">Кассовая смена</h2>
          {!shift ? (
            <div className="form-stack">
              <label>
                Касса
                <select value={registerId} onChange={(e) => setRegisterId(e.target.value)}>
                  {(registersQuery.data ?? []).map((r) => (
                    <option key={r.id} value={r.id}>
                      {r.name} ({r.code})
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Начальный остаток наличных, ₸
                <input
                  type="number"
                  min={0}
                  value={openingCash}
                  onChange={(e) => setOpeningCash(Number(e.target.value))}
                />
              </label>
              <p className="muted">
                Пересчитайте купюры в ящике и введите факт. Продажи на карту/Kaspi работают только при открытой смене.
              </p>
              <button type="button" onClick={() => openMutation.mutate()} disabled={openMutation.isPending || !registerId}>
                Открыть смену
              </button>
            </div>
          ) : (
            <div className="form-stack">
              <div className="computers-row-title">
                <strong>{shift.number}</strong>
                <span className="chip chip--ok">Открыта</span>
                <span className="chip">{shift.cashRegisterName}</span>
              </div>
              <p className="muted" style={{ margin: 0 }}>
                Открыта: {new Date(shift.openedAt).toLocaleString('ru-RU')} · старт ящика {money(shift.openingCash)} ₸
              </p>

              <div className="cash-glossary muted">
                <span>
                  <b>В ящике</b> — сколько наличных должно быть сейчас
                </span>
                <span>
                  <b>Принято оплат</b> — всё, что пробито (включая пополнения)
                </span>
                <span>
                  <b>Выручка</b> — принятое минус пополнения/предоплаты (прибыль смены)
                </span>
              </div>

              <div className="cash-stats">
                <div>
                  <span className="muted">В ящике (ожидаемо)</span>
                  <strong>{money(shift.expectedCashNow)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Принято оплат</span>
                  <strong>{money(shift.collectedNow)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Выручка смены</span>
                  <strong>{money(shift.revenueNow)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Пополнения / предоплаты</span>
                  <strong>{money(shift.depositsTotal)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Возвраты</span>
                  <strong>{money(totalRefunds)} ₸</strong>
                </div>
              </div>

              <h3 className="section-title" style={{ marginTop: 8 }}>
                Как платили
              </h3>
              <div className="cash-stats">
                <div>
                  <span className="muted">Наличные</span>
                  <strong>{money(shift.salesCash)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Карта</span>
                  <strong>{money(shift.salesCard)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Kaspi</span>
                  <strong>{money(shift.salesKaspi)} ₸</strong>
                </div>
                <div>
                  <span className="muted">Перевод</span>
                  <strong>{money(shift.salesTransfer)} ₸</strong>
                </div>
                {shift.salesOther > 0 && (
                  <div>
                    <span className="muted">Прочее / баланс</span>
                    <strong>{money(shift.salesOther)} ₸</strong>
                  </div>
                )}
              </div>

              {totalRefunds > 0 && (
                <p className="muted">
                  Возвраты: нал {money(shift.refundsCash)} · карта {money(shift.refundsCard)} · Kaspi{' '}
                  {money(shift.refundsKaspi)} · перевод {money(shift.refundsTransfer)}
                  {shift.refundsOther > 0 ? ` · прочее ${money(shift.refundsOther)}` : ''}
                </p>
              )}

              <p className="muted">
                Движения ящика: внесения +{money(shift.cashInTotal)} · изъятия −{money(shift.cashOutTotal)} · расходы −
                {money(shift.expenseTotal)}
              </p>

              <div className="pc-actions-primary cash-actions">
                <button type="button" className="ghost" onClick={() => void openXReport()} title="Промежуточный итог без закрытия">
                  Промежуточный отчёт (X)
                </button>
                <button
                  type="button"
                  className="ghost"
                  onClick={() => {
                    setError(null)
                    setMovementOpen(true)
                  }}
                >
                  Ящик: внести / изъять
                </button>
                <button
                  type="button"
                  className="danger"
                  onClick={() => {
                    setClosingCash(shift.expectedCashNow)
                    setDiscrepancyReason('')
                    setCloseOpen(true)
                  }}
                >
                  Закрыть смену
                </button>
              </div>
            </div>
          )}
          {error && !movementOpen && !closeOpen && !zOpen && !selectedReceipt && <p className="error">{error}</p>}
        </section>

        <section className="cash-card">
          <div className="computers-group-head">
            <h2 className="section-title" style={{ margin: 0 }}>
              Чеки этой смены
            </h2>
            {shift && <span className="muted">{receipts.length} · позиций {receiptItemsCount} · оплачено {money(paidTotal)} ₸</span>}
          </div>
          {!shift && <p className="muted">Откройте смену, чтобы видеть чеки</p>}
          <ul className="computers-rows cash-receipt-rows">
            {receipts.map((r) => {
              const refunded = isReceiptRefunded(r.status)
              const subject = receiptSubject(r)
              return (
                <li
                  key={r.id}
                  className={`computers-row cash-receipt-row cash-receipt-row--${subject.tone}${refunded ? ' is-refunded' : ''}`}
                >
                  <div className="cash-receipt-subject" data-tone={subject.tone}>
                    <strong className="cash-receipt-subject-title">{subject.title}</strong>
                    <span className="cash-receipt-subject-sub">{subject.subtitle}</span>
                  </div>
                  <div className="computers-row-main">
                    <div className="computers-row-title">
                      <span className="cash-receipt-number">{r.number}</span>
                      {refunded ? (
                        <span className="chip chip-warn">{receiptStatusLabel(r.status)}</span>
                      ) : (
                        <span className="chip chip--ok">Оплачен</span>
                      )}
                    </div>
                    <div className="computers-row-meta muted">
                      <span>{new Date(r.createdAt).toLocaleTimeString('ru-RU')}</span>
                      {r.payments.map((p, i) => (
                        <span key={i}>
                          {paymentMethodLabel(p.method)} {money(p.amount)} ₸
                        </span>
                      ))}
                    </div>
                  </div>
                  <div className="cash-receipt-amount">
                    <strong>{money(r.total)} ₸</strong>
                  </div>
                  <div className="computers-row-actions">
                    <button type="button" className="ghost" onClick={() => setSelectedReceiptId(r.id)}>
                      Подробнее
                    </button>
                    {shift && !refunded && (
                      <button
                        type="button"
                        className="ghost"
                        disabled={refundMutation.isPending}
                        onClick={() => {
                          setRefundComment('')
                          setRefundId(r.id)
                        }}
                      >
                        Возврат
                      </button>
                    )}
                  </div>
                </li>
              )
            })}
            {shift && receipts.length === 0 && <li className="muted cash-receipt-empty">Пока нет чеков</li>}
          </ul>
        </section>
      </div>

      <ActionDialog
        open={movementOpen}
        onClose={() => !movementMutation.isPending && setMovementOpen(false)}
        title="Наличные в ящике"
        description="Внесение — добавить размен. Изъятие — инкассация в сейф. Расход — трата клуба из ящика."
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setMovementOpen(false)} disabled={movementMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={movementMutation.isPending || movementAmount < 1} onClick={() => movementMutation.mutate()}>
              Провести
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="cash-movement-presets">
            {[
              ['CashIn', 'Внести'],
              ['CashOut', 'Изъять'],
              ['Expense', 'Расход'],
            ].map(([type, label]) => (
              <button
                key={type}
                type="button"
                className={movementType === type ? undefined : 'ghost'}
                onClick={() => setMovementType(type as typeof movementType)}
              >
                {label}
              </button>
            ))}
          </div>
          <label>
            Операция
            <select value={movementType} onChange={(e) => setMovementType(e.target.value as typeof movementType)}>
              <option value="CashIn">Внесение в ящик (размен / подкрепление)</option>
              <option value="CashOut">Изъятие из ящика (инкассация)</option>
              <option value="Expense">Расход клуба из ящика</option>
            </select>
          </label>
          <label>
            Сумма, ₸
            <input
              type="number"
              min={1}
              value={movementAmount}
              onChange={(e) => setMovementAmount(Number(e.target.value))}
            />
          </label>
          <label>
            Комментарий
            <input
              value={movementComment}
              onChange={(e) => setMovementComment(e.target.value)}
              placeholder="Например: инкассация вечер / вода / такси"
            />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={closeOpen && !!shift}
        onClose={() => !closeMutation.isPending && setCloseOpen(false)}
        title="Закрытие смены"
        description={
          shift
            ? `Пересчитайте ящик. По системе должно быть ${money(shift.expectedCashNow)} ₸. После закрытия откроется итоговый Z-отчёт.`
            : undefined
        }
        size="sm"
        closeOnBackdrop={!closeMutation.isPending}
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setCloseOpen(false)} disabled={closeMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              className="danger"
              disabled={closeMutation.isPending || (Math.abs(closeDelta) > 0.01 && !discrepancyReason.trim())}
              onClick={() => closeMutation.mutate()}
            >
              Закрыть смену
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="cash-close-summary">
            <div>
              <span className="muted">По системе</span>
              <strong>{shift ? `${money(shift.expectedCashNow)} ₸` : '—'}</strong>
            </div>
            <div>
              <span className="muted">Факт</span>
              <strong>{money(closingCash)} ₸</strong>
            </div>
          </div>
          <label>
            Фактический остаток наличных, ₸
            <input
              type="number"
              min={0}
              value={closingCash}
              onChange={(e) => setClosingCash(Number(e.target.value))}
            />
          </label>
          {Math.abs(closeDelta) > 0.01 && (
            <>
              <p className={closeDelta < 0 ? 'error' : 'muted'}>{discrepancyLabel(closeDelta)}</p>
              <label>
                Причина (обязательно при расхождении)
                <input
                  value={discrepancyReason}
                  onChange={(e) => setDiscrepancyReason(e.target.value)}
                  placeholder="Недостача / излишек — почему"
                />
              </label>
            </>
          )}
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={zOpen && !!zReport}
        onClose={() => setZOpen(false)}
        title={zReport?.closedAt ? `Итог смены (Z) · ${zReport.number}` : `Промежуточный отчёт (X) · ${zReport?.number ?? ''}`}
        description={
          zReport
            ? `${zReport.cashRegisterName} · с ${new Date(zReport.openedAt).toLocaleString('ru-RU')}${
                zReport.closedAt ? ` по ${new Date(zReport.closedAt).toLocaleString('ru-RU')}` : ' · смена ещё открыта'
              }`
            : undefined
        }
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => window.print()}>
              Печать
            </button>
            <button type="button" onClick={() => setZOpen(false)}>
              Закрыть
            </button>
          </>
        }
      >
        {zReport && (
          <div className="form-stack cash-z-report">
            <p className="muted" style={{ margin: 0 }}>
              {zReport.closedAt
                ? 'Z — финальный итог после закрытия смены.'
                : 'X — снимок «как сейчас», смена не закрывается.'}
            </p>
            <div className="cash-stats">
              <div>
                <span className="muted">Принято оплат</span>
                <strong>{money(zReport.collectedTotal)} ₸</strong>
              </div>
              <div>
                <span className="muted">Выручка (без пополнений)</span>
                <strong>{money(zReport.revenueTotal)} ₸</strong>
              </div>
              <div>
                <span className="muted">Пополнения / предоплаты</span>
                <strong>{money(zReport.depositsTotal)} ₸</strong>
              </div>
              <div>
                <span className="muted">В ящике (система)</span>
                <strong>{money(zReport.expectedCash)} ₸</strong>
              </div>
            </div>
            <div className="cash-stats">
              <div>
                <span className="muted">Старт ящика</span>
                <strong>{money(zReport.openingCash)} ₸</strong>
              </div>
              {zReport.closingCashActual != null && (
                <div>
                  <span className="muted">Факт при закрытии</span>
                  <strong>{money(zReport.closingCashActual)} ₸</strong>
                </div>
              )}
              {zReport.discrepancy != null && (
                <div>
                  <span className="muted">Сверка</span>
                  <strong className={Math.abs(zReport.discrepancy) > 0.01 ? 'error' : undefined}>
                    {discrepancyLabel(zReport.discrepancy)}
                  </strong>
                </div>
              )}
            </div>
            <p className="muted">
              Нал {money(zReport.salesCash)} · карта {money(zReport.salesCard)} · Kaspi {money(zReport.salesKaspi)} ·
              перевод {money(zReport.salesTransfer)}
              {zReport.salesOther > 0 ? ` · прочее ${money(zReport.salesOther)}` : ''}
            </p>
            <p className="muted">
              Чеков {zReport.paidReceiptCount} · возвратов {zReport.refundedReceiptCount} · внесения{' '}
              {money(zReport.cashInTotal)} · изъятия {money(zReport.cashOutTotal)} · расходы {money(zReport.expenseTotal)}
            </p>
            <h3>По типам позиций</h3>
            <ul className="zones">
              {zReport.byItemType.map((r) => (
                <li key={r.key}>
                  <span>{receiptItemTypeLabel(r.key)}</span>
                  <span className="muted">
                    {money(r.amount)} ₸ · {r.count}
                  </span>
                </li>
              ))}
            </ul>
            {zReport.byPaymentMethod.length > 0 && (
              <>
                <h3>По способам оплаты</h3>
                <ul className="zones">
                  {zReport.byPaymentMethod.map((r) => (
                    <li key={r.key}>
                      <span>{paymentMethodLabel(r.key)}</span>
                      <span className="muted">
                        {money(r.amount)} ₸ · {r.count}
                      </span>
                    </li>
                  ))}
                </ul>
              </>
            )}
            {zReport.movements.length > 0 && (
              <>
                <h3>Движения ящика</h3>
                <ul className="zones">
                  {zReport.movements.map((m, i) => (
                    <li key={`${m.createdAt}-${i}`}>
                      <span>
                        {cashMovementTypeLabel(m.type)} · {money(m.amount)} ₸
                      </span>
                      <span className="muted">{m.comment || m.category || new Date(m.createdAt).toLocaleString('ru-RU')}</span>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        )}
      </ActionDialog>

      <ActionDialog
        open={!!selectedReceipt}
        onClose={() => setSelectedReceiptId(null)}
        title={selectedReceipt ? `Чек ${selectedReceipt.number}` : 'Чек'}
        description={selectedReceipt ? `${new Date(selectedReceipt.createdAt).toLocaleString('ru-RU')} · ${receiptStatusLabel(selectedReceipt.status)}` : undefined}
        size="md"
        footer={
          <>
            {selectedReceipt && !isReceiptRefunded(selectedReceipt.status) && (
              <button
                type="button"
                className="ghost"
                onClick={() => {
                  setRefundComment('')
                  setRefundId(selectedReceipt.id)
                }}
              >
                Возврат
              </button>
            )}
            <button type="button" onClick={() => setSelectedReceiptId(null)}>
              Закрыть
            </button>
          </>
        }
      >
        {selectedReceipt && (
          <div className="form-stack">
            {(() => {
              const subject = receiptSubject(selectedReceipt)
              return (
                <div className="cash-receipt-subject cash-receipt-subject--detail" data-tone={subject.tone}>
                  <strong className="cash-receipt-subject-title">{subject.title}</strong>
                  <span className="cash-receipt-subject-sub">{subject.subtitle}</span>
                </div>
              )
            })()}
            <div className="cash-receipt-detail-head">
              <div>
                <span className="muted">Итого</span>
                <strong>{money(selectedReceipt.total)} ₸</strong>
              </div>
              <div>
                <span className="muted">Оплачено</span>
                <strong>{money(selectedReceipt.paidTotal)} ₸</strong>
              </div>
            </div>
            <ul className="receipt-list">
              {selectedReceipt.items.map((item) => (
                <li key={item.name + String(item.lineTotal)}>
                  <strong>{formatReceiptItemName(item.name)}</strong>
                  <span className="muted">
                    {receiptItemTypeLabel(String(item.itemType ?? ''))} · {item.quantity} × {money(item.unitPrice)} ₸
                  </span>
                  <strong>{money(item.lineTotal)} ₸</strong>
                </li>
              ))}
            </ul>
            <div className="cash-receipt-payments">
              {selectedReceipt.payments.map((payment, i) => (
                <span key={i} className="chip">
                  {paymentMethodLabel(payment.method)} · {money(payment.amount)} ₸
                </span>
              ))}
            </div>
            {error && <p className="error">{error}</p>}
          </div>
        )}
      </ActionDialog>

      {!!refundTarget && (
        <ActionDialog
          open={!!refundTarget}
          onClose={() => !refundMutation.isPending && setRefundId(null)}
          title={`Причина возврата · ${refundTarget.number}`}
          description="Причина попадёт в историю кассы. После этого подтвердите возврат ниже."
          size="sm"
          footer={
            <>
              <button type="button" className="ghost" onClick={() => setRefundId(null)} disabled={refundMutation.isPending}>
                Отмена
              </button>
              <button
                type="button"
                className="danger"
                disabled={refundMutation.isPending}
                onClick={() => refundTarget && refundMutation.mutate(refundTarget.id)}
              >
                {refundMutation.isPending ? 'Возврат…' : 'Подтвердить возврат'}
              </button>
            </>
          }
        >
          <div className="form-stack">
            <label>
              Причина
              <input
                value={refundComment}
                onChange={(e) => setRefundComment(e.target.value)}
                placeholder="Например: ошибочный чек / возврат товара / отмена пополнения"
              />
            </label>
            <p className="muted" style={{ margin: 0, fontSize: 12 }}>
              {refundSummary || 'Будут выполнены все связанные откаты по этому чеку.'}
            </p>
            {error && <p className="error">{error}</p>}
          </div>
        </ActionDialog>
      )}
    </div>
  )
}
