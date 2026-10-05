import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { apiFetch } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { newsCategoryLabel } from '../format'

type NewsItem = {
  id: string
  title: string
  body: string
  imageUrl?: string
  category: string
  isPublished: boolean
  isPinned: boolean
  sortOrder: number
  publishAt?: string
  expireAt?: string
  createdAt: string
}

type FormState = {
  title: string
  body: string
  category: string
  isPublished: boolean
  isPinned: boolean
  sortOrder: number
}

const emptyForm: FormState = {
  title: '',
  body: '',
  category: 'Info',
  isPublished: true,
  isPinned: false,
  sortOrder: 100,
}

const CATEGORIES = [
  { value: 'Info', label: 'Инфо' },
  { value: 'Promo', label: 'Акция' },
  { value: 'Event', label: 'Событие' },
  { value: 'Maintenance', label: 'Техработы' },
]

export function NewsPage() {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<FormState>(emptyForm)
  const [editId, setEditId] = useState<string | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState('')

  const newsQuery = useQuery({
    queryKey: ['club-news'],
    queryFn: async () => (await apiFetch<NewsItem[]>('/api/news?includeUnpublished=true')).data ?? [],
  })

  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase()
    const list = newsQuery.data ?? []
    if (!q) return list
    return list.filter(
      (n) =>
        n.title.toLowerCase().includes(q) ||
        n.body.toLowerCase().includes(q) ||
        n.category.toLowerCase().includes(q),
    )
  }, [newsQuery.data, filter])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const body = {
        title: form.title,
        body: form.body,
        imageUrl: null,
        category: form.category || 'Info',
        isPublished: form.isPublished,
        isPinned: form.isPinned,
        sortOrder: Number(form.sortOrder) || 100,
        publishAt: new Date().toISOString(),
        expireAt: null,
      }
      if (editId) return apiFetch(`/api/news/${editId}`, { method: 'PUT', body: JSON.stringify(body) })
      return apiFetch('/api/news', { method: 'POST', body: JSON.stringify(body) })
    },
    onSuccess: async () => {
      setDialogOpen(false)
      setForm(emptyForm)
      setEditId(null)
      setError(null)
      await queryClient.invalidateQueries({ queryKey: ['club-news'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка сохранения'),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/news/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setConfirmId(null)
      await queryClient.invalidateQueries({ queryKey: ['club-news'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления'),
  })

  function openCreate() {
    setEditId(null)
    setForm(emptyForm)
    setError(null)
    setDialogOpen(true)
  }

  function openEdit(n: NewsItem) {
    setEditId(n.id)
    setForm({
      title: n.title,
      body: n.body,
      category: n.category,
      isPublished: n.isPublished,
      isPinned: n.isPinned,
      sortOrder: n.sortOrder,
    })
    setError(null)
    setDialogOpen(true)
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Новости клуба</h1>
          <p className="muted" style={{ margin: '4px 0 0' }}>
            Публикуются на главной Shell у гостей. Закреплённые — сверху.
          </p>
        </div>
      </header>

      <div className="list-page">
        <div className="page-toolbar">
          <div className="page-toolbar-left">
            <input
              placeholder="Поиск…"
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              style={{ minWidth: 220 }}
            />
          </div>
          <div className="page-toolbar-right">
            <button type="button" onClick={openCreate}>
              + Новость
            </button>
          </div>
        </div>

        {newsQuery.isError && <p className="error">{(newsQuery.error as Error).message}</p>}
        {error && !dialogOpen && <p className="error">{error}</p>}

        <section className="news-admin-grid">
          {filtered.map((n) => (
            <article key={n.id} className={`news-admin-card${!n.isPublished ? ' is-off' : ''}`}>
              <div className="news-admin-meta">
                <span className={`news-cat news-cat--${n.category.toLowerCase()}`}>{newsCategoryLabel(n.category)}</span>
                {n.isPinned && <span className="news-pin">Закреплено</span>}
                {!n.isPublished && <span className="muted">черновик</span>}
              </div>
              <strong>{n.title}</strong>
              <p className="muted news-admin-body">{n.body}</p>
              <div className="order-actions">
                <button type="button" className="ghost" onClick={() => openEdit(n)}>
                  Изменить
                </button>
                <button type="button" className="ghost danger-text" onClick={() => setConfirmId(n.id)}>
                  Удалить
                </button>
              </div>
            </article>
          ))}
          {filtered.length === 0 && <p className="muted">Новостей пока нет</p>}
        </section>
      </div>

      <ActionDialog
        open={dialogOpen}
        onClose={() => !saveMutation.isPending && setDialogOpen(false)}
        title={editId ? 'Редактировать новость' : 'Новая новость'}
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setDialogOpen(false)} disabled={saveMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={saveMutation.isPending || !form.title.trim() || !form.body.trim()}
              onClick={() => saveMutation.mutate()}
            >
              {editId ? 'Сохранить' : 'Опубликовать'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Заголовок
            <input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
          </label>
          <label>
            Текст
            <textarea
              rows={5}
              value={form.body}
              onChange={(e) => setForm({ ...form, body: e.target.value })}
              style={{ resize: 'vertical' }}
            />
          </label>
          <div className="field-grid">
            <label>
              Категория
              <select value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })}>
                {CATEGORIES.map((c) => (
                  <option key={c.value} value={c.value}>
                    {c.label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Порядок
              <input
                type="number"
                value={form.sortOrder}
                onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
              />
            </label>
          </div>
          <label className="check-row">
            <input
              type="checkbox"
              checked={form.isPublished}
              onChange={(e) => setForm({ ...form, isPublished: e.target.checked })}
            />
            Опубликована на Shell
          </label>
          <label className="check-row">
            <input
              type="checkbox"
              checked={form.isPinned}
              onChange={(e) => setForm({ ...form, isPinned: e.target.checked })}
            />
            Закрепить сверху
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!confirmId}
        onClose={() => setConfirmId(null)}
        title="Удалить новость?"
        message="Новость исчезнет с главных экранов клиентов."
        confirmLabel="Удалить"
        danger
        loading={deleteMutation.isPending}
        onConfirm={() => confirmId && deleteMutation.mutate(confirmId)}
      />
    </>
  )
}
