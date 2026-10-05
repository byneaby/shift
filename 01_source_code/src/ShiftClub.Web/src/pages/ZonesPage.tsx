import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiFetch } from '../api/client'
import type { BranchDto, ZoneDto } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { formatDuration } from '../format'

type FormState = {
  name: string
  code: string
  colorHex: string
  sortOrder: string
  minSessionMinutes: string
  isActive: boolean
  kind: string
  gridColumns: string
  gridRows: string
}

const empty: FormState = {
  name: '',
  code: '',
  colorHex: '#FF6A00',
  sortOrder: '10',
  minSessionMinutes: '60',
  isActive: true,
  kind: 'Hall',
  gridColumns: '6',
  gridRows: '4',
}

export function ZonesPage() {
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

  const zonesQuery = useQuery({
    queryKey: ['zones', showInactive],
    queryFn: async () =>
      (await apiFetch<ZoneDto[]>(`/api/zones?includeInactive=${showInactive}`)).data ?? [],
  })

  const saveMutation = useMutation({
    mutationFn: async () => {
      const body = {
        branchId: branch?.id ?? null,
        name: form.name.trim(),
        code: form.code.trim(),
        colorHex: form.colorHex,
        sortOrder: Number(form.sortOrder) || 10,
        minSessionMinutes: form.minSessionMinutes ? Number(form.minSessionMinutes) : null,
        isActive: form.isActive,
        kind: form.kind,
        gridColumns: Number(form.gridColumns) || 6,
        gridRows: Number(form.gridRows) || 4,
      }
      if (editId) return apiFetch(`/api/zones/${editId}`, { method: 'PUT', body: JSON.stringify(body) })
      return apiFetch('/api/zones', { method: 'POST', body: JSON.stringify(body) })
    },
    onSuccess: async () => {
      setError(null)
      setDialogOpen(false)
      setEditId(null)
      setForm(empty)
      await queryClient.invalidateQueries({ queryKey: ['zones'] })
      await queryClient.invalidateQueries({ queryKey: ['branches'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const deactivateMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/zones/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setConfirmId(null)
      await queryClient.invalidateQueries({ queryKey: ['zones'] })
      await queryClient.invalidateQueries({ queryKey: ['branches'] })
    },
  })

  function openCreate() {
    setEditId(null)
    setForm(empty)
    setError(null)
    setDialogOpen(true)
  }

  function openEdit(z: ZoneDto) {
    setEditId(z.id)
    setForm({
      name: z.name,
      code: z.code,
      colorHex: z.colorHex || '#FF6A00',
      sortOrder: String(z.sortOrder),
      minSessionMinutes: z.minSessionMinutes != null ? String(z.minSessionMinutes) : '',
      isActive: z.isActive,
      kind: z.kind ?? 'Hall',
      gridColumns: String(z.gridColumns ?? 6),
      gridRows: String(z.gridRows ?? 4),
    })
    setError(null)
    setDialogOpen(true)
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Зоны зала</h1>
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
              + Новая зона
            </button>
          </div>
        </div>

        <section className="list-card">
          <ul className="receipt-list">
            {(zonesQuery.data ?? []).map((z) => (
              <li key={z.id}>
                <div>
                  <span
                    className="dot"
                    style={{
                      background: z.colorHex,
                      display: 'inline-block',
                      width: 12,
                      height: 12,
                      borderRadius: 99,
                      marginRight: 8,
                    }}
                  />
                  <strong>{z.name}</strong>
                  {!z.isActive && <span className="muted"> · выкл</span>}
                  <div className="muted">
                    {z.code} · {z.kind ?? 'Hall'} · сетка {z.gridColumns ?? 6}×{z.gridRows ?? 4} · порядок{' '}
                    {z.sortOrder}
                    {z.minSessionMinutes ? ` · минимум ${formatDuration(z.minSessionMinutes)}` : ''}
                  </div>
                </div>
                <div className="order-actions">
                  <button type="button" className="ghost" onClick={() => openEdit(z)}>
                    Изменить
                  </button>
                  {z.isActive && (
                    <button type="button" className="ghost" onClick={() => setConfirmId(z.id)}>
                      Выкл
                    </button>
                  )}
                </div>
              </li>
            ))}
            {(zonesQuery.data?.length ?? 0) === 0 && <li className="muted">Зон пока нет</li>}
          </ul>
        </section>
      </div>

      <ActionDialog
        open={dialogOpen}
        onClose={() => !saveMutation.isPending && setDialogOpen(false)}
        title={editId ? 'Редактировать зону' : 'Новая зона'}
        size="md"
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
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Например, VIP" />
            </label>
            <label>
              Код
              <input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} placeholder="Например, VIP" />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Цвет
              <input type="color" value={form.colorHex} onChange={(e) => setForm({ ...form, colorHex: e.target.value })} />
            </label>
            <label>
              Порядок
              <input type="number" value={form.sortOrder} onChange={(e) => setForm({ ...form, sortOrder: e.target.value })} />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Тип зоны
              <select value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })}>
                <option value="Hall">Зал (ПК)</option>
                <option value="Restroom">Уборная</option>
                <option value="Cafe">Кафе / бар</option>
                <option value="Other">Другое</option>
              </select>
            </label>
            <label>
              Мин. сеанс (мин)
              <input
                type="number"
                value={form.minSessionMinutes}
                onChange={(e) => setForm({ ...form, minSessionMinutes: e.target.value })}
              />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Сетка: колонки
              <input
                type="number"
                min={1}
                max={24}
                value={form.gridColumns}
                onChange={(e) => setForm({ ...form, gridColumns: e.target.value })}
              />
            </label>
            <label>
              Сетка: ряды
              <input
                type="number"
                min={1}
                max={24}
                value={form.gridRows}
                onChange={(e) => setForm({ ...form, gridRows: e.target.value })}
              />
            </label>
          </div>
          <label className="check-row">
            <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
            Активна
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!confirmId}
        onClose={() => setConfirmId(null)}
        title="Выключить зону?"
        message="Зона перестанет отображаться в выборе. ПК в ней останутся — зону можно включить снова через редактирование."
        confirmLabel="Выключить"
        danger
        loading={deactivateMutation.isPending}
        onConfirm={() => confirmId && deactivateMutation.mutate(confirmId)}
      />
    </>
  )
}
