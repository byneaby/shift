import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { apiFetch, getToken, newIdempotencyKey } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import * as signalR from '@microsoft/signalr'
import {
  bookingStatusLabel,
  formatDuration,
  isBookingStatus,
  isOpenBookingStatus,
  money,
} from '../format'

type AvailableComputerDto = {
  id: string
  displayName: string
  zoneId?: string
  zoneName?: string
  status: string | number
  isOnline: boolean
}

type TariffDto = {
  id: string
  name: string
  zoneId?: string
  kind?: string | number
  pricePerHour: number
  fixedDurationMinutes?: number
  fixedPrice?: number
  isAvailableNow?: boolean
}

type BookingDto = {
  id: string
  number: string
  zoneId?: string
  contactName: string
  contactPhone: string
  status: string
  startsAt: string
  endsAt: string
  durationMinutes: number
  prepaidAmount: number
  prepayMethod?: 'Cash' | 'KaspiQr' | null
  graceMinutes: number
  comment?: string
  customerName?: string
  zoneName?: string
  computers: { computerId: string; computerName?: string; zoneName?: string; gamingSessionId?: string }[]
}

function todayLocalInput() {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function nowTimeLocal() {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Локальные дата+время филиала — сервер конвертирует через TZ филиала (Asia/Almaty). */
function localBookingPayload(date: string, time: string) {
  return { localDate: date, localTime: time, startsAt: null as null }
}

function formatRange(startsAt: string, endsAt: string) {
  const opts: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' }
  return `${new Date(startsAt).toLocaleTimeString('ru-RU', opts)}–${new Date(endsAt).toLocaleTimeString('ru-RU', opts)}`
}

function localPartsFromIso(iso: string) {
  const d = new Date(iso)
  const parts = new Intl.DateTimeFormat('sv-SE', {
    timeZone: 'Asia/Almaty',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  })
    .formatToParts(d)
    .reduce<Record<string, string>>((acc, part) => {
      if (part.type !== 'literal') acc[part.type] = part.value
      return acc
    }, {})
  return {
    date: `${parts.year}-${parts.month}-${parts.day}`,
    time: `${parts.hour}:${parts.minute}`,
  }
}

export function BookingsPage() {
  const queryClient = useQueryClient()
  const [date, setDate] = useState(todayLocalInput())
  const [formDate, setFormDate] = useState(todayLocalInput())
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [editBooking, setEditBooking] = useState<BookingDto | null>(null)
  const [cancelId, setCancelId] = useState<string | null>(null)
  const [deleteId, setDeleteId] = useState<string | null>(null)
  const [startBooking, setStartBooking] = useState<BookingDto | null>(null)

  const [contactName, setContactName] = useState('')
  const [contactPhone, setContactPhone] = useState('')
  const [startTime, setStartTime] = useState('18:00')
  const [durationMinutes, setDurationMinutes] = useState(60)
  const [selectedPcs, setSelectedPcs] = useState<string[]>([])
  const [zoneFilter, setZoneFilter] = useState('')
  const [prepaidAmount, setPrepaidAmount] = useState(0)
  const [prepayMethod, setPrepayMethod] = useState<'Cash' | 'KaspiQr'>('Cash')
  const [comment, setComment] = useState('')

  const [startTariffId, setStartTariffId] = useState('')
  const [startDuration, setStartDuration] = useState(60)
  const [startPay, setStartPay] = useState<'Cash' | 'KaspiQr' | 'Balance'>('Cash')
  const [startPcIds, setStartPcIds] = useState<string[]>([])

  const bookingsQuery = useQuery({
    queryKey: ['bookings', date],
    queryFn: async () => (await apiFetch<BookingDto[]>(`/api/bookings?date=${date}`)).data ?? [],
    refetchInterval: 20000,
  })

  const availabilityQuery = useQuery({
    queryKey: ['bookings-availability', formDate, startTime, durationMinutes, zoneFilter, editBooking?.id],
    queryFn: async () => {
      const q = new URLSearchParams({
        localDate: formDate,
        localTime: startTime,
        durationMinutes: String(durationMinutes),
      })
      if (zoneFilter) q.set('zoneId', zoneFilter)
      if (editBooking?.id) q.set('excludeBookingId', editBooking.id)
      return (await apiFetch<AvailableComputerDto[]>(`/api/bookings/availability?${q}`)).data ?? []
    },
    enabled: (createOpen || !!editBooking) && durationMinutes >= 15,
    staleTime: 5000,
  })

  const tariffsQuery = useQuery({
    queryKey: ['tariffs', 'bookings'],
    queryFn: async () => (await apiFetch<TariffDto[]>('/api/tariffs')).data ?? [],
    enabled: !!startBooking,
  })

  const zones = useMemo(() => {
    const map = new Map<string, string>()
    for (const pc of availabilityQuery.data ?? []) {
      if (pc.zoneId && pc.zoneName) map.set(pc.zoneId, pc.zoneName)
    }
    return [...map.entries()].map(([id, name]) => ({ id, name }))
  }, [availabilityQuery.data])

  const availablePcs = useMemo(() => {
    const list = [...(availabilityQuery.data ?? [])]
    if (editBooking) {
      for (const c of editBooking.computers) {
        if (!list.some((x) => x.id === c.computerId)) {
          list.push({
            id: c.computerId,
            displayName: c.computerName ?? 'ПК',
            zoneId: c.zoneName ? undefined : undefined,
            zoneName: c.zoneName,
            status: 'Reserved',
            isOnline: true,
          })
        }
      }
    }
    return list
  }, [availabilityQuery.data, editBooking])

  useEffect(() => {
    if (editBooking) return
    setSelectedPcs((prev) => prev.filter((id) => availablePcs.some((p) => p.id === id)))
  }, [availablePcs, editBooking])

  useEffect(() => {
    const token = getToken()
    if (!token) return
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/staff?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()
    connection.on('BookingChanged', () => {
      void queryClient.invalidateQueries({ queryKey: ['bookings'] })
      void queryClient.invalidateQueries({ queryKey: ['bookings-availability'] })
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
    })
    void connection.start()
    return () => {
      void connection.stop()
    }
  }, [queryClient])

  const createMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/bookings', {
        method: 'POST',
        body: JSON.stringify({
          contactName,
          contactPhone,
          comment: comment || null,
          ...localBookingPayload(formDate, startTime),
          durationMinutes,
          computerIds: selectedPcs,
          prepaidAmount,
          prepayMethod: prepaidAmount > 0 ? prepayMethod : null,
          graceMinutes: 15,
          zoneId: zoneFilter || null,
          idempotencyKey: newIdempotencyKey(),
        }),
      }),
    onSuccess: async () => {
      setError(null)
      setFlash('Бронь создана')
      setContactName('')
      setContactPhone('')
      setSelectedPcs([])
      setPrepaidAmount(0)
      setComment('')
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка создания'),
  })

  const actionMutation = useMutation({
    mutationFn: async (payload: { id: string; action: 'confirm' | 'arrived' | 'cancel' }) =>
      apiFetch(`/api/bookings/${payload.id}/${payload.action}`, {
        method: 'POST',
        body: payload.action === 'cancel' ? JSON.stringify({ reason: 'Отмена с панели' }) : undefined,
      }),
    onSuccess: async () => {
      setFlash('Статус обновлён')
      setCancelId(null)
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const updateMutation = useMutation({
    mutationFn: async () => {
      if (!editBooking) throw new Error('Нет брони')
      return apiFetch(`/api/bookings/${editBooking.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          contactName,
          contactPhone,
          comment: comment || null,
          ...localBookingPayload(formDate, startTime),
          durationMinutes,
          computerIds: selectedPcs,
          graceMinutes: 15,
          zoneId: zoneFilter || null,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async () => {
      setFlash('Бронь обновлена')
      setEditBooking(null)
      setError(null)
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка сохранения'),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/bookings/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setFlash('Бронь удалена')
      setDeleteId(null)
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка удаления'),
  })

  const startMutation = useMutation({
    mutationFn: async () => {
      if (!startBooking || !startTariffId) throw new Error('Выберите тариф')
      return apiFetch(`/api/bookings/${startBooking.id}/start`, {
        method: 'POST',
        body: JSON.stringify({
          tariffId: startTariffId,
          durationMinutes: startDuration,
          paymentMethod: startPay,
          computerIds: startPcIds.length ? startPcIds : null,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async () => {
      setFlash('Сеанс(ы) запущены по брони')
      setStartBooking(null)
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка старта'),
  })

  function togglePc(id: string) {
    setSelectedPcs((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))
  }

  function openCreate() {
    setError(null)
    setEditBooking(null)
    setFormDate(date)
    setStartTime(date === todayLocalInput() ? nowTimeLocal() : '18:00')
    setContactName('')
    setContactPhone('')
    setSelectedPcs([])
    setPrepaidAmount(0)
    setComment('')
    setCreateOpen(true)
  }

  function openEdit(b: BookingDto) {
    setError(null)
    setCreateOpen(false)
    setEditBooking(b)
    const parts = localPartsFromIso(b.startsAt)
    setFormDate(parts.date)
    setStartTime(parts.time)
    setDurationMinutes(b.durationMinutes)
    setContactName(b.contactName)
    setContactPhone(b.contactPhone)
    setComment(b.comment ?? '')
    setZoneFilter(b.zoneId ?? '')
    setSelectedPcs(b.computers.map((c) => c.computerId))
    setPrepaidAmount(b.prepaidAmount)
    setPrepayMethod(b.prepayMethod === 'KaspiQr' ? 'KaspiQr' : 'Cash')
  }

  function openStart(b: BookingDto) {
    setError(null)
    setStartBooking(b)
    setStartDuration(b.durationMinutes)
    setStartPcIds(b.computers.filter((c) => !c.gamingSessionId).map((c) => c.computerId))
    setStartPay('Cash')
  }

  useEffect(() => {
    if (!startBooking || !tariffsQuery.data?.length) return
    const list = tariffsQuery.data.filter((t) => t.isAvailableNow !== false)
    if (!list.some((t) => t.id === startTariffId)) {
      setStartTariffId(list[0]?.id ?? '')
    }
  }, [startBooking, tariffsQuery.data, startTariffId])

  const upcoming = useMemo(
    () => (bookingsQuery.data ?? []).filter((b) => isOpenBookingStatus(b.status)),
    [bookingsQuery.data],
  )
  const closed = useMemo(
    () => (bookingsQuery.data ?? []).filter((b) => !isOpenBookingStatus(b.status)),
    [bookingsQuery.data],
  )

  const arrivedCount = useMemo(
    () => (bookingsQuery.data ?? []).filter((b) => isBookingStatus(b.status, 'Arrived')).length,
    [bookingsQuery.data],
  )
  const confirmedCount = useMemo(
    () => (bookingsQuery.data ?? []).filter((b) => isBookingStatus(b.status, 'Confirmed', 'Pending')).length,
    [bookingsQuery.data],
  )
  const todayCount = useMemo(() => bookingsQuery.data?.length ?? 0, [bookingsQuery.data])
  const isToday = date === todayLocalInput()

  const timelineHours = useMemo(() => {
    const hours: { hour: number; items: BookingDto[] }[] = []
    for (let h = 10; h <= 23; h++) {
      const items = (bookingsQuery.data ?? []).filter((b) => {
        const localH = new Date(b.startsAt).getHours()
        return localH === h && isOpenBookingStatus(b.status)
      })
      hours.push({ hour: h, items })
    }
    return hours
  }, [bookingsQuery.data])

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Бронирование</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Свободные ПК · soft-hold за 30 мин · старт сеанса из брони
          </p>
        </div>
        <button type="button" onClick={openCreate}>
          + Новая бронь
        </button>
      </header>
      {bookingsQuery.isError && <p className="error">{(bookingsQuery.error as Error).message}</p>}
      {flash && <p className="flash">{flash}</p>}
      {error && !createOpen && !startBooking && <p className="error">{error}</p>}

      <div className="ops-stats">
        <div className="ops-stat" style={{ ['--stat-accent' as string]: '#3ddc97' }}>
          <span>Подтверждено</span>
          <strong>{confirmedCount}</strong>
        </div>
        <div
          className="ops-stat"
          style={{ ['--stat-accent' as string]: arrivedCount ? '#fbbf24' : '#64748b' }}
        >
          <span>В клубе</span>
          <strong>{arrivedCount}</strong>
        </div>
        <div className="ops-stat" style={{ ['--stat-accent' as string]: '#60a5fa' }}>
          <span>{isToday ? 'Сегодня' : 'За день'}</span>
          <strong>{todayCount}</strong>
        </div>
        <div className="ops-stat" style={{ ['--stat-accent' as string]: '#a78bfa' }}>
          <span>Активных</span>
          <strong>{upcoming.length}</strong>
        </div>
      </div>

      <div className="list-page">
        <div className="page-toolbar">
          <div className="page-toolbar-left">
            <label>
              Дата
              <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
            </label>
            <button type="button" className="ghost" onClick={() => setDate(todayLocalInput())}>
              Сегодня
            </button>
          </div>
        </div>

        <section className="list-card" style={{ marginBottom: 16 }}>
          <h2 style={{ margin: '8px 12px', fontSize: 15 }}>Шкала дня</h2>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(110px, 1fr))', gap: 8, padding: 12 }}>
            {timelineHours.map(({ hour, items }) => (
              <div
                key={hour}
                style={{
                  border: '1px solid var(--border)',
                  borderRadius: 10,
                  padding: 8,
                  minHeight: 56,
                  background: items.length ? 'color-mix(in srgb, #a78bfa 12%, transparent)' : undefined,
                }}
              >
                <div className="muted" style={{ fontSize: 12 }}>
                  {String(hour).padStart(2, '0')}:00
                </div>
                {items.map((b) => (
                  <div key={b.id} style={{ fontSize: 12, marginTop: 4 }}>
                    {b.computers[0]?.computerName ?? b.number}
                  </div>
                ))}
              </div>
            ))}
          </div>
        </section>

        <section className="list-card">
          {bookingsQuery.isLoading && <p style={{ padding: 12 }}>Загрузка…</p>}
          <ul className="computers-rows booking-rows" style={{ margin: '8px 4px 12px' }}>
            {upcoming.length === 0 && !bookingsQuery.isLoading && (
              <li className="muted cash-receipt-empty">На этот день активных броней нет</li>
            )}
            {upcoming.map((b) => (
              <li key={b.id} className="computers-row booking-row">
                <div className="computers-row-main">
                  <div className="computers-row-title">
                    <strong>
                      {b.number} · {b.contactName}
                    </strong>
                    <span
                      className={`chip booking-status-chip${
                        isBookingStatus(b.status, 'Pending')
                          ? ' is-pending'
                          : isBookingStatus(b.status, 'Confirmed')
                            ? ' is-confirmed'
                            : isBookingStatus(b.status, 'Arrived', 'Active')
                              ? ' is-active'
                              : ''
                      }`}
                    >
                      {bookingStatusLabel(b.status)}
                    </span>
                  </div>
                  <div className="computers-row-meta muted">
                    <span>
                      {formatRange(b.startsAt, b.endsAt)} · {formatDuration(b.durationMinutes)}
                    </span>
                    <span>{b.contactPhone}</span>
                    {b.prepaidAmount > 0 && <span>предоплата {money(b.prepaidAmount)} ₸</span>}
                    {b.zoneName && <span>{b.zoneName}</span>}
                  </div>
                  <div className="computers-row-notes muted">
                    ПК:{' '}
                    {b.computers
                      .map((c) => `${c.computerName ?? c.computerId.slice(0, 6)}${c.gamingSessionId ? ' ✓' : ''}`)
                      .join(', ')}
                    {b.comment ? ` · ${b.comment}` : ''}
                  </div>
                </div>
                <div className="computers-row-actions action-chip-row">
                  {isBookingStatus(b.status, 'Pending', 'Confirmed') && (
                    <button type="button" onClick={() => actionMutation.mutate({ id: b.id, action: 'arrived' })}>
                      Гость пришёл
                    </button>
                  )}
                  {isBookingStatus(b.status, 'Pending', 'Confirmed', 'Arrived', 'Active') &&
                    b.computers.some((c) => !c.gamingSessionId) && (
                      <button type="button" onClick={() => openStart(b)}>
                        Старт сеанса
                      </button>
                    )}
                  {isBookingStatus(b.status, 'Pending', 'Confirmed', 'Arrived') && (
                    <button type="button" className="ghost" onClick={() => openEdit(b)}>
                      Изменить
                    </button>
                  )}
                  {!isBookingStatus(b.status, 'Active') && (
                    <button type="button" className="ghost" onClick={() => setCancelId(b.id)}>
                      Отменить
                    </button>
                  )}
                  {!isBookingStatus(b.status, 'Active') && !b.computers.some((c) => c.gamingSessionId) && (
                    <button type="button" className="ghost" onClick={() => setDeleteId(b.id)}>
                      Удалить
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        </section>

        {closed.length > 0 && (
          <section className="list-card" style={{ marginTop: 16 }}>
            <h2 style={{ margin: '8px 12px', fontSize: 15 }}>Закрытые</h2>
            <ul className="computers-rows booking-rows" style={{ margin: '8px 4px 12px' }}>
              {closed.map((b) => (
                <li key={b.id} className="computers-row booking-row is-closed">
                  <div className="computers-row-main">
                    <div className="computers-row-title">
                      <strong>
                        {b.number} · {b.contactName}
                      </strong>
                      <span className="chip">{bookingStatusLabel(b.status)}</span>
                    </div>
                    <div className="computers-row-meta muted">
                      <span>{formatRange(b.startsAt, b.endsAt)}</span>
                      <span>{b.computers.map((c) => c.computerName ?? 'ПК').join(', ')}</span>
                    </div>
                    {b.computers.some((c) => c.gamingSessionId) && (
                      <div className="computers-row-notes muted">
                        Есть связанный сеанс — удаление недоступно (история кассы)
                      </div>
                    )}
                  </div>
                  {!b.computers.some((c) => c.gamingSessionId) && (
                    <div className="computers-row-actions">
                      <button type="button" className="ghost" onClick={() => setDeleteId(b.id)}>
                        Удалить
                      </button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>

      <ActionDialog
        open={createOpen || !!editBooking}
        onClose={() => {
          if (createMutation.isPending || updateMutation.isPending) return
          setCreateOpen(false)
          setEditBooking(null)
        }}
        title={editBooking ? `Изменить ${editBooking.number}` : 'Новая бронь'}
        description={
          editBooking
            ? 'Можно сменить время, контакт и ПК'
            : 'Сначала время и длительность — список ПК покажет только свободные под слот'
        }
        size="lg"
        footer={
          <>
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setCreateOpen(false)
                setEditBooking(null)
              }}
              disabled={createMutation.isPending || updateMutation.isPending}
            >
              Отмена
            </button>
            <button
              type="button"
              disabled={
                createMutation.isPending ||
                updateMutation.isPending ||
                !contactName ||
                !contactPhone ||
                selectedPcs.length === 0
              }
              onClick={() => (editBooking ? updateMutation.mutate() : createMutation.mutate())}
            >
              {editBooking ? 'Сохранить' : 'Создать бронь'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="field-grid">
            <label>
              Имя
              <input value={contactName} onChange={(e) => setContactName(e.target.value)} />
            </label>
            <label>
              Телефон
              <input value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Дата
              <input type="date" value={formDate} onChange={(e) => setFormDate(e.target.value)} />
            </label>
            <label>
              Начало (когда придут)
              <input type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} />
            </label>
            <label>
              На сколько (часов)
              <input
                type="number"
                min={0.5}
                step={0.5}
                value={durationMinutes / 60}
                onChange={(e) => {
                  const hours = Number(e.target.value)
                  setDurationMinutes(Math.max(15, Math.round(hours * 60)))
                }}
              />
            </label>
            <label>
              Зона
              <select value={zoneFilter} onChange={(e) => setZoneFilter(e.target.value)}>
                <option value="">Все зоны</option>
                {zones.map((z) => (
                  <option key={z.id} value={z.id}>
                    {z.name}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <div className="order-actions">
            {[1, 2, 3, 5].map((h) => (
              <button
                key={h}
                type="button"
                className={durationMinutes === h * 60 ? undefined : 'ghost'}
                onClick={() => setDurationMinutes(h * 60)}
              >
                {h} ч
              </button>
            ))}
            <span className="muted" style={{ fontSize: 12, alignSelf: 'center' }}>
              до{' '}
              {(() => {
                const [hh, mm] = startTime.split(':').map(Number)
                if (Number.isNaN(hh)) return '—'
                const end = new Date()
                end.setHours(hh, mm || 0, 0, 0)
                end.setMinutes(end.getMinutes() + durationMinutes)
                return end.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
              })()}
              · hold за 30 мин до начала
            </span>
          </div>
          <fieldset style={{ border: '1px solid var(--border)', borderRadius: 12, padding: 12, margin: 0 }}>
            <legend>
              Свободные ПК ({availablePcs.length})
              {availabilityQuery.isFetching ? '…' : ''}
            </legend>
            {availabilityQuery.isError && (
              <p className="error">{(availabilityQuery.error as Error).message}</p>
            )}
            {availablePcs.length === 0 && !availabilityQuery.isLoading && (
              <p className="muted">Нет свободных ПК на этот слот</p>
            )}
            <div className="field-grid">
              {availablePcs.map((c) => (
                <label key={c.id} className="check-row">
                  <input type="checkbox" checked={selectedPcs.includes(c.id)} onChange={() => togglePc(c.id)} />
                  {c.displayName}
                  {c.zoneName ? ` · ${c.zoneName}` : ''}
                  {!c.isOnline ? ' · офлайн' : ''}
                </label>
              ))}
            </div>
          </fieldset>
          <div className="field-grid">
            {!editBooking && (
              <label>
                Предоплата ₸
                <input
                  type="number"
                  min={0}
                  step={100}
                  value={prepaidAmount}
                  onChange={(e) => setPrepaidAmount(Number(e.target.value))}
                />
              </label>
            )}
            {!editBooking && prepaidAmount > 0 && (
              <label>
                Способ
                <select
                  value={prepayMethod}
                  onChange={(e) => setPrepayMethod(e.target.value as typeof prepayMethod)}
                >
                  <option value="Cash">Наличные</option>
                  <option value="KaspiQr">Kaspi QR</option>
                </select>
              </label>
            )}
          </div>
          <label>
            Комментарий
            <input value={comment} onChange={(e) => setComment(e.target.value)} />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={!!startBooking}
        onClose={() => !startMutation.isPending && setStartBooking(null)}
        title="Старт по брони"
        description={
          startBooking
            ? `${startBooking.number} · ${startBooking.contactName} · ${formatRange(startBooking.startsAt, startBooking.endsAt)}`
            : undefined
        }
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setStartBooking(null)} disabled={startMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={startMutation.isPending || !startTariffId || startPcIds.length === 0}
              onClick={() => startMutation.mutate()}
            >
              Запустить
            </button>
          </>
        }
      >
        <div className="form-stack">
          {startBooking?.prepaidAmount ? (
            <p className="muted">Предоплата {startBooking.prepaidAmount} ₸ учтётся в стоимости сеанса</p>
          ) : null}
          <div className="field-grid">
            <label>
              Тариф
              <select value={startTariffId} onChange={(e) => setStartTariffId(e.target.value)}>
                {(tariffsQuery.data ?? [])
                  .filter((t) => t.isAvailableNow !== false)
                  .map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}
                      {t.kind === 'Package' || t.kind === 1 || (t.fixedPrice != null && t.fixedDurationMinutes)
                        ? ` · ${
                            t.fixedDurationMinutes != null &&
                            t.fixedDurationMinutes >= 60 &&
                            t.fixedDurationMinutes % 60 === 0
                              ? `${t.fixedDurationMinutes / 60} ч`
                              : formatDuration(t.fixedDurationMinutes ?? 0)
                          } · ${t.fixedPrice} ₸`
                        : ` · ${t.pricePerHour} ₸/ч`}
                    </option>
                  ))}
              </select>
            </label>
            <label>
              Минут
              <input
                type="number"
                min={15}
                step={15}
                value={startDuration}
                onChange={(e) => setStartDuration(Number(e.target.value))}
              />
            </label>
            <label>
              Доплата
              <select value={startPay} onChange={(e) => setStartPay(e.target.value as typeof startPay)}>
                <option value="Cash">Наличные</option>
                <option value="KaspiQr">Kaspi QR</option>
                <option value="Balance">С баланса клиента</option>
              </select>
            </label>
          </div>
          <fieldset style={{ border: '1px solid var(--border)', borderRadius: 12, padding: 12, margin: 0 }}>
            <legend>ПК для запуска</legend>
            {startBooking?.computers
              .filter((c) => !c.gamingSessionId)
              .map((c) => (
                <label key={c.computerId} className="check-row">
                  <input
                    type="checkbox"
                    checked={startPcIds.includes(c.computerId)}
                    onChange={() =>
                      setStartPcIds((prev) =>
                        prev.includes(c.computerId)
                          ? prev.filter((x) => x !== c.computerId)
                          : [...prev, c.computerId],
                      )
                    }
                  />
                  {c.computerName ?? c.computerId.slice(0, 8)}
                </label>
              ))}
          </fieldset>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!cancelId}
        onClose={() => setCancelId(null)}
        title="Отменить бронь?"
        message="Бронь будет отменена. Резерв с ПК снимется."
        confirmLabel="Отменить бронь"
        danger
        loading={actionMutation.isPending}
        onConfirm={() => cancelId && actionMutation.mutate({ id: cancelId, action: 'cancel' })}
      />

      <ConfirmDialog
        open={!!deleteId}
        onClose={() => setDeleteId(null)}
        title="Удалить бронь?"
        message="Бронь будет полностью удалена из базы. Это нельзя отменить."
        confirmLabel="Удалить"
        danger
        loading={deleteMutation.isPending}
        onConfirm={() => deleteId && deleteMutation.mutate(deleteId)}
      />
    </>
  )
}
