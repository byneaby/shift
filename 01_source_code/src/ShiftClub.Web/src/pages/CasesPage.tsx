import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { apiFetch, apiUpload } from '../api/client'
import { ActionDialog } from '../components/ActionDialog'

type AdminPrize = {
  id: string
  prizeCode: string
  name: string
  description?: string
  prizeType: number
  dropPercent: number
  costEstimateKzt: number
  payloadJson: string
  imageUrl?: string
  dailyLimit?: number | null
  totalLimit?: number | null
  requiresClaim: boolean
  sortOrder: number
  isActive: boolean
}

type AdminCase = {
  id: string
  caseCode: string
  title: string
  description?: string
  isEnabled: boolean
  showProbabilitiesToUsers: boolean
  keyCost: number
  prizes: AdminPrize[]
  activeDropPercentSum: number
}

const TYPES = [
  { value: 1, label: 'Время' },
  { value: 2, label: 'Баланс' },
  { value: 3, label: 'Бар' },
  { value: 4, label: 'Скидка' },
  { value: 5, label: 'Услуга' },
  { value: 9, label: 'Другое' },
]

const emptyPrize = {
  prizeCode: '',
  name: '',
  description: '',
  prizeType: 9,
  dropPercent: 5,
  costEstimateKzt: 0,
  payloadJson: '{}',
  imageUrl: '',
  dailyLimit: '',
  totalLimit: '',
  requiresClaim: false,
  sortOrder: 100,
  isActive: true,
}

export function CasesPage() {
  const qc = useQueryClient()
  const fileRef = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [isEnabled, setIsEnabled] = useState(true)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editId, setEditId] = useState<string | null>(null)
  const [form, setForm] = useState(emptyPrize)
  const [uploadPrizeId, setUploadPrizeId] = useState<string | null>(null)

  const adminQuery = useQuery({
    queryKey: ['case-admin'],
    queryFn: async () => (await apiFetch<AdminCase>('/api/cases/admin')).data!,
  })

  const data = adminQuery.data
  useEffect(() => {
    if (!data) return
    setTitle(data.title)
    setDescription(data.description || '')
    setIsEnabled(data.isEnabled)
  }, [data])

  const percentSum = data?.activeDropPercentSum ?? 0

  const saveCaseMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/cases/admin', {
        method: 'PUT',
        body: JSON.stringify({
          title,
          description: description || null,
          isEnabled,
          showProbabilitiesToUsers: false,
        }),
      }),
    onSuccess: async () => {
      setError(null)
      await qc.invalidateQueries({ queryKey: ['case-admin'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const savePrizeMutation = useMutation({
    mutationFn: async () => {
      const body = {
        prizeCode: form.prizeCode.trim(),
        name: form.name.trim(),
        description: form.description.trim() || null,
        prizeType: Number(form.prizeType),
        dropPercent: Number(form.dropPercent) || 0,
        costEstimateKzt: Number(form.costEstimateKzt) || 0,
        payloadJson: form.payloadJson.trim() || '{}',
        imageUrl: form.imageUrl.trim() || null,
        dailyLimit: form.dailyLimit === '' ? null : Number(form.dailyLimit),
        totalLimit: form.totalLimit === '' ? null : Number(form.totalLimit),
        requiresClaim: form.requiresClaim,
        sortOrder: Number(form.sortOrder) || 100,
        isActive: form.isActive,
      }
      if (editId) {
        return apiFetch(`/api/cases/admin/prizes/${editId}`, {
          method: 'PUT',
          body: JSON.stringify(body),
        })
      }
      return apiFetch('/api/cases/admin/prizes', {
        method: 'POST',
        body: JSON.stringify(body),
      })
    },
    onSuccess: async () => {
      setDialogOpen(false)
      setEditId(null)
      setForm(emptyPrize)
      setError(null)
      await qc.invalidateQueries({ queryKey: ['case-admin'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const toggleMutation = useMutation({
    mutationFn: async ({ id, active }: { id: string; active: boolean }) =>
      apiFetch(`/api/cases/admin/prizes/${id}/active?active=${active}`, { method: 'POST' }),
    onSuccess: async () => {
      setError(null)
      await qc.invalidateQueries({ queryKey: ['case-admin'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const uploadMutation = useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const fd = new FormData()
      fd.append('file', file)
      return apiUpload<AdminPrize>(`/api/cases/admin/prizes/${id}/image`, fd)
    },
    onSuccess: async () => {
      setError(null)
      setUploadPrizeId(null)
      await qc.invalidateQueries({ queryKey: ['case-admin'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка загрузки'),
  })

  function openCreate() {
    setEditId(null)
    setForm({
      ...emptyPrize,
      sortOrder: ((data?.prizes.length ?? 0) + 1) * 10,
    })
    setDialogOpen(true)
  }

  function openEdit(p: AdminPrize) {
    setEditId(p.id)
    setForm({
      prizeCode: p.prizeCode,
      name: p.name,
      description: p.description || '',
      prizeType: p.prizeType,
      dropPercent: p.dropPercent,
      costEstimateKzt: p.costEstimateKzt,
      payloadJson: p.payloadJson || '{}',
      imageUrl: p.imageUrl || '',
      dailyLimit: p.dailyLimit == null ? '' : String(p.dailyLimit),
      totalLimit: p.totalLimit == null ? '' : String(p.totalLimit),
      requiresClaim: p.requiresClaim,
      sortOrder: p.sortOrder,
      isActive: p.isActive,
    })
    setDialogOpen(true)
  }

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1>SHIFT CASE</h1>
          <p className="muted">Награды и шансы. Открытие кейса — только на кассе.</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <a className="btn ghost" href="/site/case.html?desk=1" target="_blank" rel="noreferrer">
            Открыть на кассе
          </a>
          <button type="button" className="btn" onClick={openCreate}>
            + Награда
          </button>
        </div>
      </div>

      {error && <div className="banner banner-error">{error}</div>}

      <section className="card" style={{ marginBottom: 16 }}>
        <h2>Кейс</h2>
        <div style={{ display: 'grid', gap: 10, marginTop: 12, maxWidth: 640 }}>
          <label>
            Название
            <input value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label>
            Описание на сайте
            <textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
          </label>
          <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <input type="checkbox" checked={isEnabled} onChange={(e) => setIsEnabled(e.target.checked)} />
            Рулетка включена
          </label>
          <button
            type="button"
            className="btn"
            style={{ width: 'fit-content' }}
            disabled={saveCaseMutation.isPending}
            onClick={() => saveCaseMutation.mutate()}
          >
            Сохранить
          </button>
        </div>
      </section>

      <section className="card">
        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'baseline' }}>
          <h2>Награды</h2>
          <span className={Math.abs(percentSum - 100) < 0.05 ? 'muted' : 'error'}>
            Сумма шансов активных: {percentSum.toFixed(2)}%
            {Math.abs(percentSum - 100) >= 0.05 ? ' (лучше = 100%)' : ''}
          </span>
        </div>
        {adminQuery.isLoading && <p className="muted">Загрузка…</p>}
        <table className="table" style={{ width: '100%', marginTop: 12 }}>
          <thead>
            <tr>
              <th>Фото</th>
              <th>Название</th>
              <th>Шанс %</th>
              <th>Активна</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {(data?.prizes ?? []).map((p) => (
              <tr key={p.id} style={{ opacity: p.isActive ? 1 : 0.45 }}>
                <td>
                  {p.imageUrl ? (
                    <img src={p.imageUrl} alt="" width={44} height={44} style={{ objectFit: 'contain' }} />
                  ) : (
                    <span className="muted">нет</span>
                  )}
                </td>
                <td>
                  <strong>{p.name}</strong>
                  <div className="muted">
                    <code>{p.prizeCode}</code>
                  </div>
                </td>
                <td>{p.dropPercent}%</td>
                <td>{p.isActive ? 'да' : 'нет'}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <button type="button" className="btn ghost" onClick={() => openEdit(p)}>
                    Изменить
                  </button>{' '}
                  <button
                    type="button"
                    className="btn ghost"
                    onClick={() => {
                      setUploadPrizeId(p.id)
                      fileRef.current?.click()
                    }}
                    disabled={uploadMutation.isPending}
                  >
                    Фото
                  </button>{' '}
                  <button
                    type="button"
                    className="btn ghost"
                    disabled={toggleMutation.isPending}
                    onClick={() => toggleMutation.mutate({ id: p.id, active: !p.isActive })}
                  >
                    {toggleMutation.isPending && toggleMutation.variables?.id === p.id
                      ? '…'
                      : p.isActive
                        ? 'Выкл'
                        : 'Вкл'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <input
          ref={fileRef}
          type="file"
          accept="image/png,image/jpeg,image/webp,image/gif"
          style={{ display: 'none' }}
          onChange={(e) => {
            const file = e.target.files?.[0]
            const id = uploadPrizeId
            e.target.value = ''
            if (!file || !id) return
            uploadMutation.mutate({ id, file })
          }}
        />
      </section>

      <ActionDialog
        open={dialogOpen}
        title={editId ? 'Редактировать награду' : 'Новая награда'}
        onClose={() => setDialogOpen(false)}
        size="lg"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setDialogOpen(false)}>
              Отмена
            </button>
            <button type="button" disabled={savePrizeMutation.isPending} onClick={() => savePrizeMutation.mutate()}>
              Сохранить
            </button>
          </>
        }
      >
        <div style={{ display: 'grid', gap: 10 }}>
          <label>
            Код
            <input
              value={form.prizeCode}
              disabled={!!editId}
              onChange={(e) => setForm({ ...form, prizeCode: e.target.value })}
            />
          </label>
          <label>
            Название
            <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
          </label>
          <label>
            Описание
            <textarea
              rows={2}
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
            />
          </label>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
            <label>
              Тип
              <select
                value={form.prizeType}
                onChange={(e) => setForm({ ...form, prizeType: Number(e.target.value) })}
              >
                {TYPES.map((t) => (
                  <option key={t.value} value={t.value}>
                    {t.label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Шанс выпадения, %
              <input
                type="number"
                min={0}
                max={100}
                step={0.01}
                value={form.dropPercent}
                onChange={(e) => setForm({ ...form, dropPercent: Number(e.target.value) })}
              />
            </label>
          </div>
          <label>
            Порядок
            <input
              type="number"
              value={form.sortOrder}
              onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
            />
          </label>
          {form.imageUrl && (
            <div>
              <img src={form.imageUrl} alt="" style={{ width: 72, height: 72, objectFit: 'contain' }} />
              <p className="muted">Фото загружается кнопкой «Фото» в таблице после сохранения награды.</p>
            </div>
          )}
          <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
            />
            Активна
          </label>
        </div>
      </ActionDialog>
    </div>
  )
}
