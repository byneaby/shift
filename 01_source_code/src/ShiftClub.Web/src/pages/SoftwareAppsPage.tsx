import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useRef, useState } from 'react'
import { apiFetch, apiUpload } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { Perm, can } from '../permissions'

type SoftwareApp = {
  id: string
  branchId: string
  name: string
  category: string
  exePath: string
  arguments?: string
  workingDirectory?: string
  iconPath?: string
  launchSoundUrl?: string
  sortOrder: number
  isActive: boolean
  minAge?: number
}

type FormState = {
  name: string
  category: string
  exePath: string
  arguments: string
  workingDirectory: string
  sortOrder: number
  isActive: boolean
}

const emptyForm: FormState = {
  name: '',
  category: 'Игры',
  exePath: '',
  arguments: '',
  workingDirectory: '',
  sortOrder: 100,
  isActive: true,
}

export function SoftwareAppsPage() {
  const canManage = can(Perm.SettingsManage)
  const queryClient = useQueryClient()
  const [form, setForm] = useState<FormState>(emptyForm)
  const [editId, setEditId] = useState<string | null>(null)
  const [dialogOpen, setDialogOpen] = useState(false)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const [filter, setFilter] = useState('')
  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const coverInputRef = useRef<HTMLInputElement | null>(null)
  const soundInputRef = useRef<HTMLInputElement | null>(null)
  const [coverAppId, setCoverAppId] = useState<string | null>(null)
  const [soundAppId, setSoundAppId] = useState<string | null>(null)

  const appsQuery = useQuery({
    queryKey: ['software-apps'],
    queryFn: async () => (await apiFetch<SoftwareApp[]>('/api/software-apps')).data ?? [],
  })

  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase()
    const list = appsQuery.data ?? []
    if (!q) return list
    return list.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        a.category.toLowerCase().includes(q) ||
        a.exePath.toLowerCase().includes(q),
    )
  }, [appsQuery.data, filter])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const body = {
        name: form.name,
        category: form.category || 'Игры',
        exePath: form.exePath,
        arguments: form.arguments || null,
        workingDirectory: form.workingDirectory || null,
        iconPath: null,
        sortOrder: Number(form.sortOrder) || 100,
        isActive: form.isActive,
        minAge: null,
      }
      if (editId) {
        return apiFetch(`/api/software-apps/${editId}`, { method: 'PUT', body: JSON.stringify(body) })
      }
      return apiFetch('/api/software-apps', { method: 'POST', body: JSON.stringify(body) })
    },
    onSuccess: async () => {
      setError(null)
      setDialogOpen(false)
      setForm(emptyForm)
      setEditId(null)
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка сохранения'),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/software-apps/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setConfirmId(null)
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления'),
  })

  const toggleMutation = useMutation({
    mutationFn: async (app: SoftwareApp) =>
      apiFetch(`/api/software-apps/${app.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          name: app.name,
          category: app.category,
          exePath: app.exePath,
          arguments: app.arguments ?? null,
          workingDirectory: app.workingDirectory ?? null,
          iconPath: app.iconPath ?? null,
          launchSoundUrl: app.launchSoundUrl ?? null,
          sortOrder: app.sortOrder,
          isActive: !app.isActive,
          minAge: app.minAge ?? null,
        }),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const coverMutation = useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const formData = new FormData()
      formData.append('file', file)
      return apiUpload<SoftwareApp>(`/api/software-apps/${id}/image`, formData)
    },
    onSuccess: async (res) => {
      setCoverAppId(null)
      setFlash('Обложка обновлена')
      const updated = res.data
      if (updated) {
        queryClient.setQueryData<SoftwareApp[]>(['software-apps'], (prev) =>
          (prev ?? []).map((a) => (a.id === updated.id ? { ...a, ...updated } : a)),
        )
      }
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка загрузки обложки'),
  })

  const soundMutation = useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const formData = new FormData()
      formData.append('file', file)
      return apiUpload<SoftwareApp>(`/api/software-apps/${id}/sound`, formData)
    },
    onSuccess: async (res) => {
      setSoundAppId(null)
      setFlash('Звук запуска обновлён')
      const updated = res.data
      if (updated) {
        queryClient.setQueryData<SoftwareApp[]>(['software-apps'], (prev) =>
          (prev ?? []).map((a) => (a.id === updated.id ? { ...a, ...updated } : a)),
        )
      }
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка загрузки звука'),
  })

  const clearSoundMutation = useMutation({
    mutationFn: async (id: string) => apiFetch<SoftwareApp>(`/api/software-apps/${id}/sound`, { method: 'DELETE' }),
    onSuccess: async (res) => {
      setFlash('Звук удалён')
      const updated = res.data
      if (updated) {
        queryClient.setQueryData<SoftwareApp[]>(['software-apps'], (prev) =>
          (prev ?? []).map((a) => (a.id === updated.id ? { ...a, ...updated } : a)),
        )
      }
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления звука'),
  })

  const importMutation = useMutation({
    mutationFn: async (payload: unknown) =>
      apiFetch<{ created: number; updated: number; skipped: number; total: number }>(
        '/api/software-apps/import?updateExisting=true',
        { method: 'POST', body: JSON.stringify(payload) },
      ),
    onSuccess: async (res) => {
      const d = res.data
      setError(null)
      setFlash(
        d
          ? `Импорт: +${d.created}, обновлено ${d.updated}, пропущено ${d.skipped} (из ${d.total})`
          : 'Импорт выполнен',
      )
      await queryClient.invalidateQueries({ queryKey: ['software-apps'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка импорта'),
  })

  function openCreate() {
    setEditId(null)
    setForm(emptyForm)
    setError(null)
    setDialogOpen(true)
  }

  function openEdit(app: SoftwareApp) {
    setEditId(app.id)
    setForm({
      name: app.name,
      category: app.category,
      exePath: app.exePath,
      arguments: app.arguments ?? '',
      workingDirectory: app.workingDirectory ?? '',
      sortOrder: app.sortOrder,
      isActive: app.isActive,
    })
    setError(null)
    setDialogOpen(true)
  }

  function pickCover(appId: string) {
    setCoverAppId(appId)
    coverInputRef.current?.click()
  }

  function pickSound(appId: string) {
    setSoundAppId(appId)
    soundInputRef.current?.click()
  }

  async function onImportFile(file: File) {
    setFlash(null)
    setError(null)
    try {
      const text = await file.text()
      const parsed = JSON.parse(text) as { apps?: unknown } | unknown[]
      if (Array.isArray(parsed)) {
        await importMutation.mutateAsync({
          schema: 'shiftclub.software-apps.v1',
          source: file.name,
          generatedAt: new Date().toISOString(),
          apps: parsed,
        })
      } else if (parsed && typeof parsed === 'object' && Array.isArray((parsed as { apps?: unknown }).apps)) {
        await importMutation.mutateAsync(parsed)
      } else {
        throw new Error('Нужен JSON формата { apps: [...] } или массив приложений')
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось прочитать JSON')
    }
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Программы ПК</h1>
          <p className="muted" style={{ margin: '4px 0 0' }}>
            {canManage
              ? 'Скан → импорт JSON. Обложка и звук запуска — на карточке каждой программы.'
              : 'Список игр и программ на клиентских ПК — только просмотр.'}
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
            <input
              ref={fileInputRef}
              type="file"
              accept="application/json,.json"
              style={{ display: 'none' }}
              onChange={(e) => {
                const f = e.target.files?.[0]
                e.target.value = ''
                if (f) void onImportFile(f)
              }}
            />
            <input
              ref={coverInputRef}
              type="file"
              accept="image/jpeg,image/png,image/webp,image/gif,.jpg,.jpeg,.png,.webp,.gif"
              style={{ display: 'none' }}
              onChange={(e) => {
                const f = e.target.files?.[0]
                e.target.value = ''
                if (f && coverAppId) coverMutation.mutate({ id: coverAppId, file: f })
              }}
            />
            <input
              ref={soundInputRef}
              type="file"
              accept="audio/mpeg,audio/wav,audio/ogg,.mp3,.wav,.ogg"
              style={{ display: 'none' }}
              onChange={(e) => {
                const f = e.target.files?.[0]
                e.target.value = ''
                if (f && soundAppId) soundMutation.mutate({ id: soundAppId, file: f })
              }}
            />
            {canManage && (
              <>
                <button
                  type="button"
                  className="ghost"
                  disabled={importMutation.isPending}
                  onClick={() => fileInputRef.current?.click()}
                >
                  Импорт JSON
                </button>
                <button type="button" onClick={openCreate}>
                  + Добавить программу
                </button>
              </>
            )}
          </div>
        </div>

        {appsQuery.isError && <p className="error">{(appsQuery.error as Error).message}</p>}
        {flash && <p className="flash">{flash}</p>}
        {error && !dialogOpen && <p className="error">{error}</p>}

        <section className="game-cover-grid">
          {filtered.map((a) => {
            const cover = a.iconPath || null
            return (
              <article key={a.id} className={`game-cover-card${a.isActive ? '' : ' is-off'}`}>
                <div className="game-cover-media">
                  {cover ? (
                    <img key={cover} src={cover} alt="" />
                  ) : (
                    <span className="game-cover-letter">{a.name.slice(0, 1).toUpperCase()}</span>
                  )}
                  <div className="game-cover-shade">
                    <strong>{a.name}</strong>
                    <span>
                      {a.category}
                      {a.launchSoundUrl ? ' · 🔊' : ''}
                      {!a.isActive ? ' · выкл' : ''}
                    </span>
                  </div>
                </div>
                {canManage && (
                  <div className="game-cover-actions">
                    <button type="button" className="ghost" onClick={() => pickCover(a.id)} disabled={coverMutation.isPending}>
                      Фото
                    </button>
                    <button type="button" className="ghost" onClick={() => pickSound(a.id)} disabled={soundMutation.isPending}>
                      {a.launchSoundUrl ? 'Звук ✓' : 'Звук'}
                    </button>
                    {a.launchSoundUrl && (
                      <button
                        type="button"
                        className="ghost"
                        disabled={clearSoundMutation.isPending}
                        onClick={() => clearSoundMutation.mutate(a.id)}
                      >
                        Без звука
                      </button>
                    )}
                    <button type="button" className="ghost" onClick={() => openEdit(a)}>
                      Изменить
                    </button>
                    <button
                      type="button"
                      className="ghost"
                      disabled={toggleMutation.isPending}
                      onClick={() => toggleMutation.mutate(a)}
                    >
                      {a.isActive ? 'Выкл' : 'Вкл'}
                    </button>
                    <button type="button" className="ghost danger-text" onClick={() => setConfirmId(a.id)}>
                      Удалить
                    </button>
                  </div>
                )}
                <p className="game-cover-path muted" title={a.exePath}>
                  {a.exePath}
                </p>
              </article>
            )
          })}
          {filtered.length === 0 && <p className="muted">Пока пусто</p>}
        </section>
      </div>

      <ActionDialog
        open={dialogOpen}
        onClose={() => !saveMutation.isPending && setDialogOpen(false)}
        title={editId ? 'Редактировать программу' : 'Новая программа'}
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setDialogOpen(false)} disabled={saveMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={saveMutation.isPending || !form.name || !form.exePath}
              onClick={() => saveMutation.mutate()}
            >
              {editId ? 'Сохранить' : 'Добавить'}
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
              Категория
              <input
                value={form.category}
                onChange={(e) => setForm({ ...form, category: e.target.value })}
                placeholder="Игры / Утилиты"
              />
            </label>
          </div>
          <label>
            Путь к exe
            <input
              value={form.exePath}
              onChange={(e) => setForm({ ...form, exePath: e.target.value })}
              placeholder="Путь к .exe, например D:\Games\Game\game.exe"
            />
          </label>
          <label>
            Аргументы
            <input value={form.arguments} onChange={(e) => setForm({ ...form, arguments: e.target.value })} />
          </label>
          <label>
            Рабочая папка
            <input
              value={form.workingDirectory}
              onChange={(e) => setForm({ ...form, workingDirectory: e.target.value })}
            />
          </label>
          <div className="field-grid">
            <label>
              Порядок
              <input
                type="number"
                value={form.sortOrder}
                onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) })}
              />
            </label>
            <label className="check-row">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              />
              Активна
            </label>
          </div>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!confirmId}
        onClose={() => setConfirmId(null)}
        title="Удалить программу?"
        message="Программа будет удалена из каталога навсегда. Обложку можно загрузить заново после повторного добавления."
        confirmLabel="Удалить"
        danger
        loading={deleteMutation.isPending}
        onConfirm={() => confirmId && deleteMutation.mutate(confirmId)}
      />
    </>
  )
}
