import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { apiFetch, newIdempotencyKey } from '../api/client'
import { payKaspiTerminalIfReady } from '../kaspiPos'
import { can, Perm } from '../permissions'
import { ActionDialog } from './ActionDialog'
import { FloorCustomerPick, type FloorCustomerLite } from './FloorCustomerPick'

/** Базовая цена ключа SHIFT CASE (₸). Меняется на кассе при необходимости. */
export const CASE_KEY_DEFAULT_PRICE = 1000

type CaseKeyCustomer = FloorCustomerLite & { caseKeysBalance?: number }

type BuyKeysResult = {
  receiptNumber?: string
  keysGranted?: number
  keysBalance?: number
  paidTotal?: number
}

export function FloorQuickCaseKeyDialog({
  open,
  onClose,
  presetCustomerId,
  presetCustomerName,
  title,
  onSold,
}: {
  open: boolean
  onClose: () => void
  presetCustomerId?: string | null
  presetCustomerName?: string | null
  title?: string
  onSold?: (message: string) => void
}) {
  const queryClient = useQueryClient()
  const allowed = can(Perm.CustomersDeposit)
  const [query, setQuery] = useState('')
  const [picked, setPicked] = useState<CaseKeyCustomer | null>(null)
  const [qty, setQty] = useState(1)
  const [unitPrice, setUnitPrice] = useState(CASE_KEY_DEFAULT_PRICE)
  const [pay, setPay] = useState<'Cash' | 'KaspiQr' | 'Card'>('Cash')
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)

  const total = qty * unitPrice

  const searchQuery = useQuery({
    queryKey: ['floor-case-key-customers', query],
    queryFn: async () =>
      (await apiFetch<CaseKeyCustomer[]>(`/api/customers?q=${encodeURIComponent(query)}`)).data ?? [],
    enabled: open && allowed && query.trim().length >= 2 && !picked,
  })

  const detailQuery = useQuery({
    queryKey: ['floor-case-key-customer', presetCustomerId],
    queryFn: async () =>
      (await apiFetch<CaseKeyCustomer>(`/api/customers/${presetCustomerId}`)).data ?? null,
    enabled: open && allowed && !!presetCustomerId && !picked,
  })

  useEffect(() => {
    if (!open) return
    setQty(1)
    setUnitPrice(CASE_KEY_DEFAULT_PRICE)
    setPay('Cash')
    setError(null)
    setFlash(null)
    if (presetCustomerId) {
      setQuery(presetCustomerName ?? '')
      setPicked(null)
    } else {
      setQuery('')
      setPicked(null)
    }
  }, [open, presetCustomerId, presetCustomerName])

  useEffect(() => {
    if (!open || !detailQuery.data || picked) return
    if (detailQuery.data.id !== presetCustomerId) return
    setPicked(detailQuery.data)
    setQuery(detailQuery.data.fullName)
  }, [open, detailQuery.data, presetCustomerId, picked])

  const sellMutation = useMutation({
    mutationFn: async () => {
      if (!picked) throw new Error('Выберите клиента')
      if (qty < 1) throw new Error('Укажите количество')
      if (unitPrice < 100) throw new Error('Цена ключа минимум 100 ₸')
      if (pay === 'KaspiQr') {
        await payKaspiTerminalIfReady(total)
      }
      return apiFetch<BuyKeysResult>('/api/cases/buy-keys', {
        method: 'POST',
        body: JSON.stringify({
          customerId: picked.id,
          quantity: qty,
          unitPrice,
          paymentMethod: pay,
          idempotencyKey: newIdempotencyKey(),
          comment: `Быстрая продажа ключа CASE · ${picked.fullName}`,
        }),
      })
    },
    onSuccess: async (res) => {
      const d = res.data
      const msg = d
        ? `Ключи: +${d.keysGranted} · чек ${d.receiptNumber} · ${Number(d.paidTotal ?? total).toLocaleString('ru-RU')} ₸ · остаток ${d.keysBalance}`
        : `Продано · ${total.toLocaleString('ru-RU')} ₸`
      setFlash(msg)
      setError(null)
      if (picked && d?.keysBalance != null) {
        setPicked({ ...picked, caseKeysBalance: d.keysBalance })
      }
      onSold?.(msg)
      await queryClient.invalidateQueries({ queryKey: ['customers'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      await queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
      await queryClient.invalidateQueries({ queryKey: ['customer', picked?.id] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Не удалось продать ключ'),
  })

  if (!allowed) return null

  return (
    <ActionDialog
      open={open}
      onClose={() => {
        if (sellMutation.isPending) return
        onClose()
      }}
      title={title ?? 'Продать ключ CASE'}
      description="Чек попадёт в кассу и отчёты. После продажи можно открыть кейс у кассы."
      size="sm"
      footer={
        <>
          <button type="button" className="ghost" onClick={onClose} disabled={sellMutation.isPending}>
            Закрыть
          </button>
          <button
            type="button"
            disabled={!picked || sellMutation.isPending || qty < 1 || unitPrice < 100}
            onClick={() => sellMutation.mutate()}
          >
            {sellMutation.isPending ? 'Продаём…' : `Продать · ${total.toLocaleString('ru-RU')} ₸`}
          </button>
        </>
      }
    >
      <div className="form-stack">
        <FloorCustomerPick
          query={query}
          onQuery={(q) => {
            setQuery(q)
            setPicked(null)
            setFlash(null)
            setError(null)
          }}
          picked={picked}
          onPick={(c) => {
            setPicked(c)
            setQuery(c.fullName)
            setFlash(null)
            setError(null)
          }}
          onClear={() => {
            setPicked(null)
            setQuery('')
            setFlash(null)
          }}
          hits={searchQuery.data ?? []}
          hitsLoading={searchQuery.isFetching}
          allowCreate
        />

        {picked && (
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Ключей на аккаунте:{' '}
            <strong>{picked.caseKeysBalance ?? '—'}</strong>
          </p>
        )}

        <div>
          <span className="muted" style={{ fontSize: 13 }}>
            Количество
          </span>
          <div className="action-chip-row" style={{ marginTop: 6 }}>
            {[1, 2, 3, 5].map((n) => (
              <button
                key={n}
                type="button"
                className={qty === n ? undefined : 'ghost'}
                onClick={() => setQty(n)}
              >
                {n}
              </button>
            ))}
          </div>
        </div>

        <div>
          <span className="muted" style={{ fontSize: 13 }}>
            Цена за ключ
          </span>
          <div className="action-chip-row" style={{ marginTop: 6 }}>
            {[500, 1000, 1500, 2000].map((p) => (
              <button
                key={p}
                type="button"
                className={unitPrice === p ? undefined : 'ghost'}
                onClick={() => setUnitPrice(p)}
              >
                {p.toLocaleString('ru-RU')} ₸
              </button>
            ))}
          </div>
        </div>

        <label>
          Оплата
          <select value={pay} onChange={(e) => setPay(e.target.value as typeof pay)}>
            <option value="Cash">Наличные</option>
            <option value="KaspiQr">Kaspi QR</option>
            <option value="Card">Карта</option>
          </select>
        </label>

        <p style={{ margin: 0, fontWeight: 600 }}>
          Итого: {total.toLocaleString('ru-RU')} ₸
          {qty > 1 ? ` (${qty} × ${unitPrice.toLocaleString('ru-RU')})` : ''}
        </p>

        {flash && <p className="flash">{flash}</p>}
        {error && <p className="error">{error}</p>}
      </div>
    </ActionDialog>
  )
}
