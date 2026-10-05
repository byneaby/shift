import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { apiFetch } from '../api/client'
import type { BranchDto } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { formatDuration, money } from '../format'

type Tariff = {
  id: string
  branchId: string
  zoneId?: string
  zoneName?: string
  name: string
  code: string
  description?: string
  kind: 'Hourly' | 'Package' | 'Free' | number
  billingMode: 'PerMinute' | 'Block15' | 'Block30' | number
  durationMode?: 'FixedDuration' | 'TimeWindow' | number
  pricePerHour: number
  minCharge: number
  fixedDurationMinutes?: number
  fixedPrice?: number
  minDurationMinutes?: number
  maxDurationMinutes?: number
  daysOfWeekMask: number
  availableFrom?: string
  availableTo?: string
  allowPause: boolean
  isActive: boolean
  sortOrder: number
  colorHex?: string
  isAvailableNow: boolean
  windowEndsAtLocal?: string
  remainingMinutesInWindow?: number
  salePreview?: string
}

type FormState = {
  name: string
  code: string
  description: string
  zoneId: string
  kind: Tariff['kind']
  billingMode: Tariff['billingMode']
  durationMode: 'FixedDuration' | 'TimeWindow'
  pricePerHour: string
  minCharge: string
  fixedDurationMinutes: string
  fixedPrice: string
  minDurationMinutes: string
  maxDurationMinutes: string
  daysOfWeekMask: number
  availableFrom: string
  availableTo: string
  allowPause: boolean
  isActive: boolean
  sortOrder: string
}

const DAYS = [
  { bit: 0, label: 'Пн' },
  { bit: 1, label: 'Вт' },
  { bit: 2, label: 'Ср' },
  { bit: 3, label: 'Чт' },
  { bit: 4, label: 'Пт' },
  { bit: 5, label: 'Сб' },
  { bit: 6, label: 'Вс' },
] as const

const empty: FormState = {
  name: '',
  code: '',
  description: '',
  zoneId: '',
  kind: 'Hourly',
  billingMode: 'PerMinute',
  durationMode: 'FixedDuration',
  pricePerHour: '500',
  minCharge: '0',
  fixedDurationMinutes: '',
  fixedPrice: '',
  minDurationMinutes: '60',
  maxDurationMinutes: '',
  daysOfWeekMask: 127,
  availableFrom: '',
  availableTo: '',
  allowPause: true,
  isActive: true,
  sortOrder: '100',
}

function daysLabel(mask: number) {
  return DAYS.filter((d) => (mask & (1 << d.bit)) !== 0)
    .map((d) => d.label)
    .join('·')
}

export function TariffsPage() {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<FormState>(empty)
  const [editId, setEditId] = useState<string | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [showInactive, setShowInactive] = useState(true)

  const branchesQuery = useQuery({
    queryKey: ['branches'],
    queryFn: async () => (await apiFetch<BranchDto[]>('/api/branches')).data ?? [],
  })
  const branch = branchesQuery.data?.[0]

  const tariffsQuery = useQuery({
    queryKey: ['tariffs-admin', showInactive],
    queryFn: async () =>
      (await apiFetch<Tariff[]>(`/api/tariffs?includeInactive=${showInactive}`)).data ?? [],
  })

  const zones = useMemo(() => branch?.zones ?? [], [branch])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const body = {
        branchId: branch?.id ?? null,
        zoneId: form.zoneId || null,
        name: form.name.trim(),
        code: form.code.trim(),
        description: form.description.trim() || null,
        kind: form.kind,
        billingMode: form.billingMode,
        durationMode: form.durationMode,
        pricePerHour: Number(form.pricePerHour) || 0,
        minCharge: Number(form.minCharge) || 0,
        fixedDurationMinutes:
          form.durationMode === 'TimeWindow'
            ? null
            : form.fixedDurationMinutes
              ? Number(form.fixedDurationMinutes)
              : null,
        fixedPrice: form.fixedPrice ? Number(form.fixedPrice) : null,
        minDurationMinutes: form.minDurationMinutes ? Number(form.minDurationMinutes) : null,
        maxDurationMinutes: form.maxDurationMinutes ? Number(form.maxDurationMinutes) : null,
        daysOfWeekMask: form.daysOfWeekMask,
        availableFrom: form.availableFrom || null,
        availableTo: form.availableTo || null,
        allowPause: form.allowPause,
        isActive: form.isActive,
        sortOrder: Number(form.sortOrder) || 100,
        colorHex: null,
      }
      if (editId) {
        return apiFetch(`/api/tariffs/${editId}`, { method: 'PUT', body: JSON.stringify(body) })
      }
      return apiFetch('/api/tariffs', { method: 'POST', body: JSON.stringify(body) })
    },
    onSuccess: async () => {
      setError(null)
      setDialogOpen(false)
      setEditId(null)
      setForm(empty)
      await queryClient.invalidateQueries({ queryKey: ['tariffs-admin'] })
      await queryClient.invalidateQueries({ queryKey: ['tariffs'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const deactivateMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/tariffs/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setConfirmId(null)
      await queryClient.invalidateQueries({ queryKey: ['tariffs-admin'] })
      await queryClient.invalidateQueries({ queryKey: ['tariffs'] })
    },
  })

  function openCreate() {
    setEditId(null)
    setForm(empty)
    setError(null)
    setDialogOpen(true)
  }

  function openEdit(t: Tariff) {
    const kindName =
      t.kind === 1 || t.kind === 'Package'
        ? 'Package'
        : t.kind === 3 || t.kind === 'Free'
          ? 'Free'
          : 'Hourly'
    const billingName =
      t.billingMode === 1 || t.billingMode === 'Block15'
        ? 'Block15'
        : t.billingMode === 2 || t.billingMode === 'Block30'
          ? 'Block30'
          : 'PerMinute'
    const durationModeName =
      t.durationMode === 1 || t.durationMode === 'TimeWindow' ? 'TimeWindow' : 'FixedDuration'
    setEditId(t.id)
    setForm({
      name: t.name,
      code: t.code,
      description: t.description ?? '',
      zoneId: t.zoneId ?? '',
      kind: kindName,
      billingMode: billingName,
      durationMode: durationModeName,
      pricePerHour: String(t.pricePerHour),
      minCharge: String(t.minCharge),
      fixedDurationMinutes: t.fixedDurationMinutes != null ? String(t.fixedDurationMinutes) : '',
      fixedPrice: t.fixedPrice != null ? String(t.fixedPrice) : '',
      minDurationMinutes: t.minDurationMinutes != null ? String(t.minDurationMinutes) : '',
      maxDurationMinutes: t.maxDurationMinutes != null ? String(t.maxDurationMinutes) : '',
      daysOfWeekMask: t.daysOfWeekMask || 127,
      availableFrom: t.availableFrom ?? '',
      availableTo: t.availableTo ?? '',
      allowPause: t.allowPause,
      isActive: t.isActive,
      sortOrder: String(t.sortOrder),
    })
    setError(null)
    setDialogOpen(true)
  }

  function toggleDay(bit: number) {
    setForm((f) => {
      const next = f.daysOfWeekMask ^ (1 << bit)
      return { ...f, daysOfWeekMask: next === 0 ? f.daysOfWeekMask : next }
    })
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Тарифы</h1>
        </div>
      </header>

      <div className="list-page">
        <div className="page-toolbar">
          <div className="page-toolbar-left">
            <label className="check-row">
              <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
              Показывать выключенные
            </label>
          </div>
          <div className="page-toolbar-right">
            <button type="button" onClick={openCreate}>
              + Новый тариф
            </button>
          </div>
        </div>

        {tariffsQuery.isError && <p className="error">{(tariffsQuery.error as Error).message}</p>}

        <section className="list-card">
          <ul className="receipt-list">
            {(tariffsQuery.data ?? []).map((t) => (
              <li key={t.id}>
                <div>
                  <strong>{t.name}</strong>
                  {!t.isActive && <span className="muted"> · выкл</span>}
                  {t.isAvailableNow ? (
                    <span className="flash"> · сейчас доступен</span>
                  ) : (
                    <span className="muted"> · сейчас вне окна</span>
                  )}
                  <div className="muted">
                    {t.durationMode === 'TimeWindow' || t.durationMode === 1
                      ? `Интервал · ${t.availableFrom ?? '—'}–${t.availableTo ?? '—'} · ${money(t.fixedPrice ?? 0)} ₸`
                      : t.kind === 'Package' || t.kind === 1 || (t.fixedPrice != null && t.fixedDurationMinutes)
                        ? `Пакет · ${formatDuration(t.fixedDurationMinutes ?? 0)} · ${money(t.fixedPrice ?? 0)} ₸`
                        : `Почасовой · ${money(t.pricePerHour)} ₸/ч`}
                    {t.zoneName ? ` · ${t.zoneName}` : ' · все зоны'}
                  </div>
                  <div className="muted">
                    {daysLabel(t.daysOfWeekMask)}
                    {t.availableFrom || t.availableTo
                      ? ` · ${t.availableFrom ?? '00:00'}–${t.availableTo ?? '24:00'}`
                      : ' · круглосуточно'}
                    {t.allowPause ? ' · пауза ✓' : ' · без паузы'}
                  </div>
                </div>
                <div className="order-actions">
                  <button type="button" className="ghost" onClick={() => openEdit(t)}>
                    Изменить
                  </button>
                  {t.isActive && (
                    <button type="button" className="ghost" onClick={() => setConfirmId(t.id)}>
                      Выкл
                    </button>
                  )}
                </div>
              </li>
            ))}
            {(tariffsQuery.data?.length ?? 0) === 0 && <li className="muted">Тарифов пока нет</li>}
          </ul>
        </section>
      </div>

      <ActionDialog
        open={dialogOpen}
        onClose={() => !saveMutation.isPending && setDialogOpen(false)}
        title={editId ? 'Редактировать тариф' : 'Новый тариф'}
        size="lg"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setDialogOpen(false)} disabled={saveMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={!form.name || !form.code || saveMutation.isPending}
              onClick={() => saveMutation.mutate()}
            >
              {editId ? 'Сохранить' : 'Создать'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="field-grid">
            <label>
              Название
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
            </label>
            <label>
              Код
              <input
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
                placeholder="Код, напр. STD_HOUR"
              />
            </label>
          </div>
          <label>
            Описание
            <input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
          </label>
          <div className="field-grid">
            <label>
              Зона
              <select value={form.zoneId} onChange={(e) => setForm({ ...form, zoneId: e.target.value })}>
                <option value="">Все зоны</option>
                {zones.map((z) => (
                  <option key={z.id} value={z.id}>
                    {z.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Тип
              <select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value as FormState['kind'] })}>
                <option value="Hourly">Почасовой</option>
                <option value="Package">Пакет (фикс.)</option>
                <option value="Free">Бесплатный</option>
              </select>
            </label>
          </div>
          <label>
            Режим длительности
            <select
              value={form.durationMode}
              onChange={(e) => {
                const durationMode = e.target.value as FormState['durationMode']
                setForm({
                  ...form,
                  durationMode,
                  kind: durationMode === 'TimeWindow' ? 'Package' : form.kind,
                })
              }}
            >
              <option value="FixedDuration">Фиксированная продолжительность</option>
              <option value="TimeWindow">Временной интервал (день / ночь)</option>
            </select>
          </label>
          {form.durationMode === 'TimeWindow' && (
            <p className="muted" style={{ margin: 0 }}>
              Сеанс всегда заканчивается в указанное время окончания, независимо от момента покупки. Полная стоимость
              списывается даже если до конца окна осталось мало времени.
            </p>
          )}
          <label>
            Биллинг
            <select
              value={form.billingMode}
              onChange={(e) => setForm({ ...form, billingMode: e.target.value as FormState['billingMode'] })}
            >
              <option value="PerMinute">Поминутно</option>
              <option value="Block15">Блок 15 мин</option>
              <option value="Block30">Блок 30 мин</option>
            </select>
          </label>
          {form.kind !== 'Package' && form.kind !== 'Free' && form.durationMode !== 'TimeWindow' && (
            <div className="field-grid">
              <label>
                ₸ / час
                <input
                  type="number"
                  value={form.pricePerHour}
                  onChange={(e) => setForm({ ...form, pricePerHour: e.target.value })}
                />
              </label>
              <label>
                Мин. списание
                <input type="number" value={form.minCharge} onChange={(e) => setForm({ ...form, minCharge: e.target.value })} />
              </label>
              <label>
                Мин. минут
                <input
                  type="number"
                  value={form.minDurationMinutes}
                  onChange={(e) => setForm({ ...form, minDurationMinutes: e.target.value })}
                />
              </label>
              <label>
                Макс. минут
                <input
                  type="number"
                  value={form.maxDurationMinutes}
                  onChange={(e) => setForm({ ...form, maxDurationMinutes: e.target.value })}
                />
              </label>
            </div>
          )}
          {form.durationMode === 'TimeWindow' && (
            <div className="field-grid">
              <label>
                Начало интервала (ЧЧ:ММ)
                <input
                  value={form.availableFrom}
                  onChange={(e) => setForm({ ...form, availableFrom: e.target.value })}
                  placeholder="00:00"
                />
              </label>
              <label>
                Окончание интервала
                <input
                  value={form.availableTo}
                  onChange={(e) => setForm({ ...form, availableTo: e.target.value })}
                  placeholder="08:00"
                />
              </label>
              <label>
                Стоимость
                <input type="number" value={form.fixedPrice} onChange={(e) => setForm({ ...form, fixedPrice: e.target.value })} />
              </label>
            </div>
          )}
          {form.kind === 'Package' && form.durationMode !== 'TimeWindow' && (
            <div className="field-grid">
              <label>
                Длительность пакета (мин)
                <input
                  type="number"
                  value={form.fixedDurationMinutes}
                  onChange={(e) => setForm({ ...form, fixedDurationMinutes: e.target.value })}
                />
              </label>
              <label>
                Цена пакета
                <input type="number" value={form.fixedPrice} onChange={(e) => setForm({ ...form, fixedPrice: e.target.value })} />
              </label>
            </div>
          )}
          <div>
            <p className="muted" style={{ marginBottom: 8 }}>
              Дни недели
            </p>
            <div className="order-actions">
              {DAYS.map((d) => (
                <button
                  key={d.bit}
                  type="button"
                  className={(form.daysOfWeekMask & (1 << d.bit)) !== 0 ? undefined : 'ghost'}
                  onClick={() => toggleDay(d.bit)}
                >
                  {d.label}
                </button>
              ))}
            </div>
          </div>
          {form.durationMode !== 'TimeWindow' && (
          <div className="field-grid">
            <label>
              Доступен с (ЧЧ:ММ)
              <input
                value={form.availableFrom}
                onChange={(e) => setForm({ ...form, availableFrom: e.target.value })}
                placeholder="10:00"
              />
            </label>
            <label>
              Доступен до
              <input
                value={form.availableTo}
                onChange={(e) => setForm({ ...form, availableTo: e.target.value })}
                placeholder="22:00"
              />
            </label>
          </div>
          )}
          <div className="field-grid">
            <label className="check-row">
              <input
                type="checkbox"
                checked={form.allowPause}
                onChange={(e) => setForm({ ...form, allowPause: e.target.checked })}
              />
              Пауза / остаток времени
            </label>
            <label className="check-row">
              <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
              Активен
            </label>
          </div>
          <label>
            Порядок
            <input type="number" value={form.sortOrder} onChange={(e) => setForm({ ...form, sortOrder: e.target.value })} />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!confirmId}
        onClose={() => setConfirmId(null)}
        title="Выключить тариф?"
        message="Тариф перестанет предлагаться при старте сеанса. Его можно снова включить через редактирование."
        confirmLabel="Выключить"
        danger
        loading={deactivateMutation.isPending}
        onConfirm={() => confirmId && deactivateMutation.mutate(confirmId)}
      />
    </>
  )
}
