import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, newIdempotencyKey } from '../api/client'
import type { BranchDto } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { buildPcDisplay } from '../occupancy'
import { can, Perm } from '../permissions'

type ComputerDto = {
  id: string
  displayName?: string
  windowsName: string
  zoneId?: string
  zoneName?: string
  ipAddress?: string
  macAddress?: string
  status: string | number
  isApproved: boolean
  registrationCode?: string
  clientVersion?: string
  isMaintenance: boolean
  notes?: string
  lastSeenAt?: string
  occupancy?: string
  occupancyDetail?: string
  currentSessionId?: string
  remainingSeconds?: number
  sessionStatus?: string | number
  stationKind?: string | number
}

type ZoneDto = { id: string; name: string; code?: string }

type FilterKey = 'all' | 'pending' | 'free' | 'busy' | 'reserved' | 'offline'

function isConsoleStation(c: ComputerDto) {
  return c.stationKind === 'Console' || c.stationKind === 1
}

function formatLastSeen(iso?: string) {
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return null
  const mins = Math.floor((Date.now() - d.getTime()) / 60_000)
  if (mins < 1) return 'только что'
  if (mins < 60) return `${mins} мин назад`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours} ч назад`
  return d.toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
}

function matchesFilter(c: ComputerDto, filter: FilterKey) {
  if (filter === 'all') return true
  if (filter === 'pending') return !c.isApproved
  if (!c.isApproved) return false
  const view = buildPcDisplay(c)
  if (filter === 'free') return view.kind === 'Free' && !view.offlineBadge
  if (filter === 'busy') return view.kind === 'Busy'
  if (filter === 'reserved') return view.kind === 'Reserved'
  if (filter === 'offline') return view.offlineBadge || view.kind === 'Offline'
  return true
}

export function ComputersPage() {
  const queryClient = useQueryClient()
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const [filter, setFilter] = useState<FilterKey>('all')
  const [query, setQuery] = useState('')
  const [editId, setEditId] = useState<string | null>(null)
  const [deleteId, setDeleteId] = useState<string | null>(null)
  const [revokeId, setRevokeId] = useState<string | null>(null)
  const [displayName, setDisplayName] = useState('')
  const [zoneId, setZoneId] = useState('')
  const [notes, setNotes] = useState('')
  const [maintenance, setMaintenance] = useState(false)
  const [checkedIds, setCheckedIds] = useState<Set<string>>(new Set())
  const [bulkConfirm, setBulkConfirm] = useState<{ type: string; label: string } | null>(null)
  const [bulkBusy, setBulkBusy] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)
  const [createName, setCreateName] = useState('')
  const [createZoneId, setCreateZoneId] = useState('')

  const computersQuery = useQuery({
    queryKey: ['computers'],
    queryFn: async () => (await apiFetch<ComputerDto[]>('/api/computers')).data ?? [],
    refetchInterval: 8000,
  })

  const branchQuery = useQuery({
    queryKey: ['branches'],
    queryFn: async () => (await apiFetch<BranchDto[]>('/api/branches')).data ?? [],
  })

  const zones: ZoneDto[] = useMemo(
    () =>
      (branchQuery.data?.[0]?.zones ?? []).map((z) => ({
        id: z.id,
        name: z.name,
        code: z.code,
      })),
    [branchQuery.data],
  )

  const all = computersQuery.data ?? []

  const counts = useMemo(() => {
    let pending = 0
    let free = 0
    let busy = 0
    let reserved = 0
    let offline = 0
    for (const c of all) {
      if (!c.isApproved) {
        pending++
        continue
      }
      const view = buildPcDisplay(c)
      if (view.kind === 'Busy') busy++
      else if (view.kind === 'Reserved') reserved++
      else if (view.offlineBadge || view.kind === 'Offline') offline++
      else if (view.kind === 'Free') free++
    }
    return { pending, free, busy, reserved, offline, total: all.length }
  }, [all])

  const list = useMemo(() => {
    const q = query.trim().toLowerCase()
    return all
      .filter((c) => matchesFilter(c, filter))
      .filter((c) => {
        if (!q) return true
        const hay = [c.displayName, c.windowsName, c.zoneName, c.macAddress, c.ipAddress, c.clientVersion]
          .filter(Boolean)
          .join(' ')
          .toLowerCase()
        return hay.includes(q)
      })
      .sort((a, b) => {
        if (a.isApproved !== b.isApproved) return a.isApproved ? 1 : -1
        const an = (a.displayName ?? a.windowsName).localeCompare(b.displayName ?? b.windowsName, 'ru')
        return an
      })
  }, [all, filter, query])

  const grouped = useMemo(() => {
    const map = new Map<string, { title: string; items: ComputerDto[] }>()
    for (const c of list) {
      const key = !c.isApproved ? '__pending__' : c.zoneId ?? '__none__'
      const title = !c.isApproved ? 'Ожидают подтверждения' : c.zoneName ?? 'Без зоны'
      const g = map.get(key) ?? { title, items: [] }
      g.items.push(c)
      map.set(key, g)
    }
    return [...map.values()]
  }, [list])

  const editTarget = all.find((c) => c.id === editId) ?? null

  const updateMutation = useMutation({
    mutationFn: async () =>
      apiFetch(`/api/computers/${editId}`, {
        method: 'PUT',
        body: JSON.stringify({
          displayName: displayName.trim() || null,
          zoneId: zoneId || null,
          isMaintenance: maintenance,
          notes: notes.trim() || null,
        }),
      }),
    onSuccess: async () => {
      setFlash('ПК обновлён')
      setError(null)
      setEditId(null)
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/computers/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setFlash('ПК удалён')
      setDeleteId(null)
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка удаления'),
  })

  const revokeMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/computers/${id}/revoke`, { method: 'POST', body: '{}' }),
    onSuccess: async () => {
      setFlash('Регистрация сброшена — клиент запросит подтверждение снова')
      setRevokeId(null)
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка сброса'),
  })

  const createMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/computers/manual', {
        method: 'POST',
        body: JSON.stringify({
          displayName: createName.trim(),
          zoneId: createZoneId,
          stationKind: 'Console',
        }),
      }),
    onSuccess: async () => {
      setFlash('PS5 добавлена — запустите сеанс с карты зала')
      setCreateOpen(false)
      setCreateName('')
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
      await queryClient.invalidateQueries({ queryKey: ['branches'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка создания'),
  })

  function toggleChecked(id: string) {
    setCheckedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  function toggleAll(ids: string[]) {
    setCheckedIds((prev) => {
      const allIn = ids.every((id) => prev.has(id))
      const next = new Set(prev)
      if (allIn) ids.forEach((id) => next.delete(id))
      else ids.forEach((id) => next.add(id))
      return next
    })
  }

  const checkedApproved = useMemo(
    () => all.filter((c) => c.isApproved && checkedIds.has(c.id) && !isConsoleStation(c)),
    [all, checkedIds],
  )

  async function bulkCommand(type: string) {
    const ids = checkedApproved.map((c) => c.id)
    if (ids.length === 0) return
    setBulkBusy(true)
    setError(null)
    try {
      const results = await Promise.allSettled(
        ids.map((id) =>
          apiFetch(`/api/computers/${id}/commands`, {
            method: 'POST',
            body: JSON.stringify({ type, payloadJson: null, idempotencyKey: newIdempotencyKey() }),
          }),
        ),
      )
      const failed = results.filter((r) => r.status === 'rejected').length
      const ok = ids.length - failed
      setFlash(failed > 0 ? `Команда: ${ok} из ${ids.length} ПК` : `Команда отправлена на ${ok} ПК`)
      setBulkConfirm(null)
      setCheckedIds(new Set())
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Ошибка команды')
    } finally {
      setBulkBusy(false)
    }
  }

  function openEdit(c: ComputerDto) {
    setError(null)
    setEditId(c.id)
    setDisplayName(c.displayName ?? c.windowsName)
    setZoneId(c.zoneId ?? zones[0]?.id ?? '')
    setNotes(c.notes ?? '')
    setMaintenance(!!c.isMaintenance)
  }

  const filters: { key: FilterKey; label: string; count: number; color?: string }[] = [
    { key: 'all', label: 'Все', count: counts.total },
    { key: 'pending', label: 'Ожидают', count: counts.pending, color: '#fbbf24' },
    { key: 'free', label: 'Свободны', count: counts.free, color: '#2dd4bf' },
    { key: 'busy', label: 'Заняты', count: counts.busy, color: '#38bdf8' },
    { key: 'reserved', label: 'Бронь', count: counts.reserved, color: '#a78bfa' },
    { key: 'offline', label: 'Офлайн', count: counts.offline, color: '#64748b' },
  ]

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Компьютеры</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Статусы как в зале · PS5 (ручной учёт) · подтверждение новых ПК
          </p>
        </div>
        <div className="order-actions">
          {can(Perm.ComputersManage) && (
            <button
              type="button"
              onClick={() => {
                setError(null)
                setCreateName('')
                const ps5 = zones.find((z) => z.code === 'PS5' || /playstation|ps5/i.test(z.name))
                setCreateZoneId(ps5?.id ?? zones[0]?.id ?? '')
                setCreateOpen(true)
              }}
            >
              + PS5
            </button>
          )}
          <Link className="ghost" to="/floor">
            К залу ↗
          </Link>
        </div>
      </header>

      {flash && <p className="flash">{flash}</p>}
      {error && !editTarget && <p className="error">{error}</p>}
      {computersQuery.isError && <p className="error">{(computersQuery.error as Error).message}</p>}

      <div className="computers-stats">
        {filters.slice(1).map((f) => (
          <button
            key={f.key}
            type="button"
            className={`computers-stat${filter === f.key ? ' is-active' : ''}`}
            style={{ ['--stat-accent' as string]: f.color }}
            onClick={() => setFilter(filter === f.key ? 'all' : f.key)}
          >
            <i className="floor-stat-dot" style={{ background: f.color }} />
            <span>{f.label}</span>
            <strong>{f.count}</strong>
          </button>
        ))}
      </div>

      <div className="page-toolbar computers-toolbar">
        <div className="order-actions computers-filters">
          {filters.map((f) => (
            <button
              key={f.key}
              type="button"
              className={filter === f.key ? undefined : 'ghost'}
              onClick={() => setFilter(f.key)}
            >
              {f.label}
              <span className="computers-filter-count">{f.count}</span>
            </button>
          ))}
        </div>
        <label className="computers-search">
          <span className="sr-only">Поиск</span>
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Поиск: имя, MAC, IP, зона…"
          />
        </label>
      </div>

      {checkedIds.size > 0 && (
        <div className="computers-bulk-bar">
          <span>
            Выбрано <strong>{checkedApproved.length}</strong> ПК
          </span>
          <div className="order-actions">
            <button type="button" className="ghost" onClick={() => setBulkConfirm({ type: 'Restart', label: 'Перезагрузить' })}>
              Перезагрузить
            </button>
            <button type="button" className="ghost" onClick={() => setBulkConfirm({ type: 'Shutdown', label: 'Выключить' })}>
              Выключить
            </button>
            <button type="button" className="ghost" onClick={() => setBulkConfirm({ type: 'Lock', label: 'Заблокировать' })}>
              Блокировать
            </button>
            <button type="button" className="ghost" onClick={() => setBulkConfirm({ type: 'Unlock', label: 'Разблокировать' })}>
              Разблокировать
            </button>
            <button type="button" className="ghost" onClick={() => setBulkConfirm({ type: 'RestartShell', label: 'Перезапустить Shell' })}>
              Рестарт Shell
            </button>
            <button type="button" className="ghost" onClick={() => setBulkConfirm({ type: 'ForceUpdate', label: 'Обновить клиент' })}>
              Обновить Shell
            </button>
            <button type="button" className="ghost" onClick={() => setCheckedIds(new Set())}>
              Снять всё
            </button>
          </div>
        </div>
      )}

      <section className="computers-list">
        {grouped.map((group) => {
          const groupIds = group.items.filter((c) => c.isApproved).map((c) => c.id)
          return (
          <div key={group.title} className="computers-group">
            <div className="computers-group-head">
              <label className="check-row" style={{ gap: 8, cursor: 'pointer' }}>
                {groupIds.length > 0 && (
                  <input
                    type="checkbox"
                    checked={groupIds.length > 0 && groupIds.every((id) => checkedIds.has(id))}
                    onChange={() => toggleAll(groupIds)}
                  />
                )}
                <h2>{group.title}</h2>
              </label>
              <span className="muted">{group.items.length}</span>
            </div>
            <ul className="computers-rows">
              {group.items.map((c) => {
                const view = c.isApproved
                  ? buildPcDisplay(c)
                  : {
                      kind: 'Setup' as const,
                      color: '#fbbf24',
                      statusLabel: 'Ожидает',
                      offlineBadge: false,
                      timeText: null,
                      timeTone: null,
                      isPaused: false,
                      title: 'Ожидает подтверждения',
                    }
                const seen = formatLastSeen(c.lastSeenAt)
                return (
                  <li key={c.id} className={`computers-row${!c.isApproved ? ' is-pending' : ''}${checkedIds.has(c.id) ? ' is-checked' : ''}`}>
                    {c.isApproved && (
                      <input
                        type="checkbox"
                        className="computers-row-check"
                        checked={checkedIds.has(c.id)}
                        onChange={() => toggleChecked(c.id)}
                      />
                    )}
                    <div className="computers-row-main">
                      <div className="computers-row-title">
                        <strong>{c.displayName ?? c.windowsName}</strong>
                        {c.isMaintenance && <span className="chip chip-warn">Техработы</span>}
                      </div>
                      <div className="computers-row-status">
                        <span className="pc-status-chip" style={{ ['--pc-accent' as string]: view.color }}>
                          {view.statusLabel}
                          {view.offlineBadge && <i className="pc-offline-badge">офф</i>}
                        </span>
                        {view.timeText && (
                          <span
                            className={`pc-status-time${view.timeTone === 'busy-warn' ? ' is-warn' : ''}${
                              view.timeTone === 'booking' ? ' is-booking' : ''
                            }${view.timeTone === 'pause' ? ' is-pause' : ''}`}
                          >
                            {view.timeText}
                          </span>
                        )}
                      </div>
                      <div className="computers-row-meta muted">
                        {c.windowsName !== (c.displayName ?? c.windowsName) && <span>{c.windowsName}</span>}
                        {c.zoneName && <span>{c.zoneName}</span>}
                        <span>MAC {c.macAddress ?? '—'}</span>
                        <span>IP {c.ipAddress ?? '—'}</span>
                        {c.clientVersion && <span>v{c.clientVersion}</span>}
                        {seen && <span>связь {seen}</span>}
                      </div>
                      {c.notes && <div className="computers-row-notes muted">{c.notes}</div>}
                    </div>
                    <div className="computers-row-actions">
                      {can(Perm.ComputersManage) && !c.isApproved ? (
                        <Link className="computers-approve-btn" to="/floor">
                          Подтвердить в зале
                        </Link>
                      ) : null}
                      {can(Perm.ComputersManage) && c.isApproved ? (
                        <button type="button" onClick={() => openEdit(c)}>
                          Изменить
                        </button>
                      ) : null}
                      {can(Perm.ComputersManage) && (
                      <details className="computers-more">
                        <summary>Ещё</summary>
                        <div className="computers-more-menu">
                          {c.isApproved && (
                            <button type="button" className="ghost" onClick={() => setRevokeId(c.id)}>
                              Сброс регистрации
                            </button>
                          )}
                          <button type="button" className="ghost danger-text" onClick={() => setDeleteId(c.id)}>
                            Удалить
                          </button>
                        </div>
                      </details>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          </div>
          )
        })}
        {list.length === 0 && (
          <div className="computers-empty muted">
            {query.trim()
              ? 'Ничего не найдено по запросу'
              : filter === 'pending'
                ? 'Нет ПК, ожидающих подтверждения'
                : 'Нет компьютеров в этом фильтре'}
          </div>
        )}
      </section>

      <ActionDialog
        open={createOpen}
        onClose={() => !createMutation.isPending && setCreateOpen(false)}
        title="Добавить PS5"
        description="Только учёт времени с кассы. Удалённого управления нет — сотрудник сам скажет гостю о времени."
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setCreateOpen(false)} disabled={createMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              onClick={() => createMutation.mutate()}
              disabled={createMutation.isPending || !createName.trim() || !createZoneId}
            >
              {createMutation.isPending ? 'Создание…' : 'Добавить'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Название
            <input
              value={createName}
              onChange={(e) => setCreateName(e.target.value)}
              placeholder="PS5-1"
              autoFocus
            />
          </label>
          <label>
            Зона
            <select value={createZoneId} onChange={(e) => setCreateZoneId(e.target.value)}>
              {zones.map((z) => (
                <option key={z.id} value={z.id}>
                  {z.name}
                  {z.code ? ` (${z.code})` : ''}
                </option>
              ))}
            </select>
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={!!editTarget}
        onClose={() => !updateMutation.isPending && setEditId(null)}
        title={editTarget && isConsoleStation(editTarget) ? 'Параметры PS5' : 'Параметры ПК'}
        description={editTarget ? `${editTarget.windowsName}${editTarget.macAddress ? ` · ${editTarget.macAddress}` : ''}${isConsoleStation(editTarget) ? ' · консоль' : ''}` : undefined}
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setEditId(null)} disabled={updateMutation.isPending}>
              Отмена
            </button>
            <button type="button" onClick={() => updateMutation.mutate()} disabled={updateMutation.isPending}>
              Сохранить
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Имя на карте
            <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
          </label>
          <label>
            Зона
            <select value={zoneId} onChange={(e) => setZoneId(e.target.value)}>
              {zones.map((z) => (
                <option key={z.id} value={z.id}>
                  {z.name}
                </option>
              ))}
            </select>
          </label>
          <label className="check-row">
            <input type="checkbox" checked={maintenance} onChange={(e) => setMaintenance(e.target.checked)} />
            Техработы
          </label>
          <label>
            Заметки
            <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!deleteId}
        onClose={() => !deleteMutation.isPending && setDeleteId(null)}
        title="Удалить ПК?"
        message="ПК пропадёт из зала. История сеансов сохранится. Повторная регистрация с тем же MAC снова попадёт в ожидание."
        confirmLabel="Удалить"
        danger
        loading={deleteMutation.isPending}
        onConfirm={() => deleteId && deleteMutation.mutate(deleteId)}
      />

      <ConfirmDialog
        open={!!revokeId}
        onClose={() => !revokeMutation.isPending && setRevokeId(null)}
        title="Сбросить регистрацию?"
        message="Токен клиента аннулируется. ПК снова появится в «Ожидают» после перезапуска Shell."
        confirmLabel="Сбросить"
        danger
        loading={revokeMutation.isPending}
        onConfirm={() => revokeId && revokeMutation.mutate(revokeId)}
      />

      <ConfirmDialog
        open={!!bulkConfirm}
        onClose={() => !bulkBusy && setBulkConfirm(null)}
        title={`${bulkConfirm?.label ?? 'Команда'} · ${checkedApproved.length} ПК`}
        message={`${bulkConfirm?.label ?? 'Действие'} будет отправлено на ${checkedApproved.length} компьютер(ов): ${checkedApproved.map((c) => c.displayName ?? c.windowsName).join(', ')}`}
        confirmLabel={bulkConfirm?.label ?? 'Отправить'}
        danger={bulkConfirm?.type === 'Shutdown' || bulkConfirm?.type === 'Restart'}
        loading={bulkBusy}
        onConfirm={() => bulkConfirm && bulkCommand(bulkConfirm.type)}
      />
    </>
  )
}
