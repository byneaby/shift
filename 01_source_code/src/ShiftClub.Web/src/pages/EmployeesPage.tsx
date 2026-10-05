import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiUpload } from '../api/client'
import { ActionDialog } from '../components/ActionDialog'
import {
  activeFlagLabel,
  auditActionLabel,
  auditActionTone,
  employeePayTypeLabel,
  payrollStatusLabel,
  payrollStatusName,
  payrollTypeLabel,
  rolesLabel,
  workShiftStatusLabel,
  workShiftStatusName,
} from '../format'

type Tab = 'people' | 'journal' | 'shifts' | 'payroll' | 'roles' | 'system'

type MeEmployee = { roles?: string[] }

type AuditItem = {
  id: string
  createdAt: string
  employeeId?: string | null
  employeeName?: string | null
  employeeLogin?: string | null
  action: string
  entityType: string
  entityId?: string | null
  detailsJson?: string | null
  ipAddress?: string | null
}

type AuditPage = {
  items: AuditItem[]
  total: number
  page: number
  pageSize: number
}

type StaffDto = {
  id: string
  login: string
  displayName: string
  email?: string | null
  phone?: string | null
  isActive: boolean
  payType: string | number
  hourlyRate: number
  monthlySalary: number
  shiftRate: number
  lastLoginAt?: string | null
  roles: string[]
}

type RoleDto = { id: string; code: string; name: string; description?: string; permissions: string[] }

type EditForm = {
  id: string
  login: string
  displayName: string
  email: string
  phone: string
  isActive: boolean
  payType: string
  hourlyRate: number
  monthlySalary: number
  shiftRate: number
  roleCodes: string[]
}

function payTypeCode(value: string | number | undefined) {
  if (value === 0 || value === '0' || value === 'Hourly') return 'Hourly'
  if (value === 1 || value === '1' || value === 'FixedMonthly') return 'FixedMonthly'
  if (value === 2 || value === '2' || value === 'PerShift') return 'PerShift'
  return 'Hourly'
}

function toEditForm(e: StaffDto): EditForm {
  return {
    id: e.id,
    login: e.login,
    displayName: e.displayName,
    email: e.email ?? '',
    phone: e.phone ?? '',
    isActive: e.isActive,
    payType: payTypeCode(e.payType),
    hourlyRate: e.hourlyRate,
    monthlySalary: e.monthlySalary,
    shiftRate: e.shiftRate,
    roleCodes: e.roles?.length ? [...e.roles] : ['cashier'],
  }
}

type ShiftDto = {
  id: string
  employeeId: string
  employeeName: string
  workDate: string
  plannedStart: string
  plannedEnd: string
  status: string | number
  actualStartAt?: string | null
  actualEndAt?: string | null
  breakMinutes: number
  workedHours: number
  comment?: string
}

type AccrualDto = {
  id: string
  employeeId: string
  employeeName: string
  type: string | number
  status: string | number
  amount: number
  periodFrom: string
  periodTo: string
  basis?: string
}

type SummaryDto = {
  employeeId: string
  employeeName: string
  accrued: number
  paid: number
  remaining: number
  workedHours: number
  shiftsCompleted: number
}

function today() {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function monthStart() {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-01`
}

export function EmployeesPage() {
  const queryClient = useQueryClient()
  const me = JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as MeEmployee | null
  const isOwner = (me?.roles ?? []).some((r) => String(r).toLowerCase() === 'owner')
  const [shellAdminPwd, setShellAdminPwd] = useState('')
  const [shellAdminPwd2, setShellAdminPwd2] = useState('')
  const [shellAdminMsg, setShellAdminMsg] = useState<string | null>(null)
  const [loginBgMsg, setLoginBgMsg] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('people')
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)

  const [createForm, setCreateForm] = useState({
    login: '',
    displayName: '',
    password: 'Cashier123!',
    phone: '',
    roleCodes: ['cashier'] as string[],
    payType: 'Hourly',
    hourlyRate: 1500,
    monthlySalary: 0,
    shiftRate: 0,
  })

  const [shiftForm, setShiftForm] = useState({
    employeeId: '',
    workDate: today(),
    plannedStart: '10:00',
    plannedEnd: '22:00',
  })

  const [payForm, setPayForm] = useState({
    employeeId: '',
    type: 'Bonus',
    amount: 10000,
    basis: '',
    periodFrom: monthStart(),
    periodTo: today(),
  })
  const [createOpen, setCreateOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const [editForm, setEditForm] = useState<EditForm | null>(null)
  const [passwordForm, setPasswordForm] = useState({ id: '', name: '', password: '', password2: '' })
  const [shiftOpen, setShiftOpen] = useState(false)
  const [payOpen, setPayOpen] = useState(false)
  const [peopleQ, setPeopleQ] = useState('')
  const [peopleFilter, setPeopleFilter] = useState<'all' | 'active' | 'off'>('all')
  const [journalEmployeeId, setJournalEmployeeId] = useState('')
  const [journalAction, setJournalAction] = useState('')
  const [journalSearch, setJournalSearch] = useState('')
  const [journalTechnical, setJournalTechnical] = useState(false)
  const [journalPage, setJournalPage] = useState(1)
  const [journalFrom, setJournalFrom] = useState(() => {
    const d = new Date()
    d.setDate(d.getDate() - 7)
    return d.toISOString().slice(0, 10)
  })
  const [journalTo, setJournalTo] = useState(today())

  const employeesQuery = useQuery({
    queryKey: ['employees'],
    queryFn: async () => (await apiFetch<StaffDto[]>('/api/employees')).data ?? [],
  })

  const rolesQuery = useQuery({
    queryKey: ['roles'],
    queryFn: async () => (await apiFetch<RoleDto[]>('/api/employees/roles')).data ?? [],
  })

  const shiftsQuery = useQuery({
    queryKey: ['work-shifts'],
    queryFn: async () => (await apiFetch<ShiftDto[]>('/api/employees/shifts')).data ?? [],
    enabled: tab === 'shifts',
  })

  const payrollQuery = useQuery({
    queryKey: ['payroll'],
    queryFn: async () => (await apiFetch<AccrualDto[]>('/api/employees/payroll')).data ?? [],
    enabled: tab === 'payroll',
  })

  const summaryQuery = useQuery({
    queryKey: ['payroll-summary'],
    queryFn: async () => (await apiFetch<SummaryDto[]>('/api/employees/payroll/summary')).data ?? [],
    enabled: tab === 'payroll' || tab === 'people',
  })

  const journalQuery = useQuery({
    queryKey: [
      'audit-journal',
      journalFrom,
      journalTo,
      journalEmployeeId,
      journalAction,
      journalSearch,
      journalTechnical,
      journalPage,
    ],
    queryFn: async () => {
      const qs = new URLSearchParams({
        page: String(journalPage),
        pageSize: '40',
        technical: journalTechnical ? 'true' : 'false',
      })
      if (journalFrom) qs.set('from', new Date(`${journalFrom}T00:00:00`).toISOString())
      if (journalTo) qs.set('to', new Date(`${journalTo}T23:59:59`).toISOString())
      if (journalEmployeeId) qs.set('employeeId', journalEmployeeId)
      if (journalAction) qs.set('action', journalAction)
      if (journalSearch.trim()) qs.set('q', journalSearch.trim())
      return (await apiFetch<AuditPage>(`/api/audit?${qs}`)).data!
    },
    enabled: tab === 'journal',
    placeholderData: (prev) => prev,
  })

  const auditActionsQuery = useQuery({
    queryKey: ['audit-actions', journalTechnical],
    queryFn: async () =>
      (await apiFetch<string[]>(`/api/audit/actions?technical=${journalTechnical ? 'true' : 'false'}`)).data ?? [],
    enabled: tab === 'journal',
  })

  const staff = employeesQuery.data ?? []
  const filteredStaff = useMemo(() => {
    const q = peopleQ.trim().toLowerCase()
    return staff.filter((e) => {
      if (peopleFilter === 'active' && !e.isActive) return false
      if (peopleFilter === 'off' && e.isActive) return false
      if (!q) return true
      return (
        e.displayName.toLowerCase().includes(q) ||
        e.login.toLowerCase().includes(q) ||
        (e.phone ?? '').toLowerCase().includes(q) ||
        rolesLabel(e.roles).toLowerCase().includes(q)
      )
    })
  }, [staff, peopleQ, peopleFilter])

  const staffStats = useMemo(() => {
    const active = staff.filter((e) => e.isActive).length
    const owners = staff.filter((e) => e.roles.some((r) => String(r).toLowerCase() === 'owner')).length
    const cashiers = staff.filter((e) => e.roles.some((r) => String(r).toLowerCase() === 'cashier')).length
    const summary = summaryQuery.data ?? []
    const shifts = summary.reduce((sum, item) => sum + item.shiftsCompleted, 0)
    const hours = summary.reduce((sum, item) => sum + item.workedHours, 0)
    return { total: staff.length, active, owners, cashiers, shifts, hours }
  }, [staff, summaryQuery.data])

  const summaryByEmployee = useMemo(
    () => new Map((summaryQuery.data ?? []).map((item) => [item.employeeId, item])),
    [summaryQuery.data],
  )

  const selectedEmployeeId = useMemo(
    () => shiftForm.employeeId || staff[0]?.id || '',
    [shiftForm.employeeId, staff],
  )

  function initials(name: string) {
    const parts = name.trim().split(/\s+/).filter(Boolean)
    if (parts.length === 0) return '?'
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
    return (parts[0][0] + parts[1][0]).toUpperCase()
  }

  function detailsPreview(json?: string | null) {
    if (!json) return null
    try {
      const obj = JSON.parse(json) as Record<string, unknown>
      return Object.entries(obj)
        .slice(0, 4)
        .map(([k, v]) => `${k}: ${String(v)}`)
        .join(' · ')
    } catch {
      return json.slice(0, 120)
    }
  }

  const createMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/employees', {
        method: 'POST',
        body: JSON.stringify({
          login: createForm.login,
          displayName: createForm.displayName,
          password: createForm.password,
          phone: createForm.phone || null,
          email: null,
          payType: createForm.payType,
          hourlyRate: createForm.hourlyRate,
          monthlySalary: createForm.monthlySalary,
          shiftRate: createForm.shiftRate,
          roleCodes: createForm.roleCodes,
        }),
      }),
    onSuccess: async () => {
      setFlash('Сотрудник создан')
      setError(null)
      setCreateOpen(false)
      setCreateForm((f) => ({ ...f, login: '', displayName: '', phone: '' }))
      await queryClient.invalidateQueries({ queryKey: ['employees'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const updateMutation = useMutation({
    mutationFn: async (form: EditForm) => {
      const res = await apiFetch(`/api/employees/${form.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          displayName: form.displayName.trim(),
          email: form.email.trim() || null,
          phone: form.phone.trim() || null,
          isActive: form.isActive,
          payType: form.payType,
          hourlyRate: form.hourlyRate,
          monthlySalary: form.monthlySalary,
          shiftRate: form.shiftRate,
          roleCodes: form.roleCodes,
        }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сохранить')
      return res.data
    },
    onSuccess: async () => {
      setFlash('Сотрудник сохранён')
      setError(null)
      setEditOpen(false)
      setEditForm(null)
      await queryClient.invalidateQueries({ queryKey: ['employees'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const passwordMutation = useMutation({
    mutationFn: async () => {
      if (passwordForm.password.trim().length < 6) throw new Error('Минимум 6 символов')
      if (passwordForm.password !== passwordForm.password2) throw new Error('Пароли не совпадают')
      const res = await apiFetch(`/api/employees/${passwordForm.id}/password`, {
        method: 'POST',
        body: JSON.stringify({ newPassword: passwordForm.password }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сменить пароль')
    },
    onSuccess: () => {
      setFlash('Пароль обновлён')
      setError(null)
      setPasswordOpen(false)
      setPasswordForm({ id: '', name: '', password: '', password2: '' })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const toggleActiveMutation = useMutation({
    mutationFn: async (e: StaffDto) => {
      const form = toEditForm(e)
      form.isActive = !e.isActive
      const res = await apiFetch(`/api/employees/${form.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          displayName: form.displayName,
          email: form.email || null,
          phone: form.phone || null,
          isActive: form.isActive,
          payType: form.payType,
          hourlyRate: form.hourlyRate,
          monthlySalary: form.monthlySalary,
          shiftRate: form.shiftRate,
          roleCodes: form.roleCodes,
        }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось изменить статус')
    },
    onSuccess: async (_d, e) => {
      setFlash(e.isActive ? 'Сотрудник отключён' : 'Сотрудник включён')
      setError(null)
      await queryClient.invalidateQueries({ queryKey: ['employees'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const deleteMutation = useMutation({
    mutationFn: async (e: StaffDto) => {
      const res = await apiFetch(`/api/employees/${e.id}`, { method: 'DELETE' })
      if (!res.success) throw new Error(res.message || 'Не удалось удалить')
    },
    onSuccess: async () => {
      setFlash('Аккаунт удалён')
      setError(null)
      await queryClient.invalidateQueries({ queryKey: ['employees'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  function openEdit(e: StaffDto) {
    setError(null)
    setEditForm(toEditForm(e))
    setEditOpen(true)
  }

  function openPassword(e: StaffDto) {
    setError(null)
    setPasswordForm({ id: e.id, name: e.displayName, password: '', password2: '' })
    setPasswordOpen(true)
  }

  function toggleRole(code: string, selected: string[], onChange: (next: string[]) => void) {
    if (selected.includes(code)) {
      if (selected.length <= 1) return
      onChange(selected.filter((c) => c !== code))
    } else {
      onChange([...selected, code])
    }
  }

  const shiftMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/employees/shifts', {
        method: 'POST',
        body: JSON.stringify({
          employeeId: selectedEmployeeId,
          workDate: shiftForm.workDate,
          plannedStart: `${shiftForm.plannedStart}:00`,
          plannedEnd: `${shiftForm.plannedEnd}:00`,
          comment: null,
        }),
      }),
    onSuccess: async () => {
      setFlash('Смена добавлена')
      setShiftOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['work-shifts'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const shiftAction = useMutation({
    mutationFn: async (payload: { id: string; action: 'clock-in' | 'clock-out' | 'absent' | 'cancel' | 'break-start' | 'break-end' }) =>
      apiFetch(`/api/employees/shifts/${payload.id}/${payload.action}`, {
        method: 'POST',
        body: JSON.stringify({ comment: null }),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['work-shifts'] })
      await queryClient.invalidateQueries({ queryKey: ['payroll-summary'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const payMutation = useMutation({
    mutationFn: async () =>
      apiFetch('/api/employees/payroll', {
        method: 'POST',
        body: JSON.stringify({
          employeeId: payForm.employeeId || staff[0]?.id,
          periodFrom: payForm.periodFrom,
          periodTo: payForm.periodTo,
          type: payForm.type,
          amount: payForm.amount,
          basis: payForm.basis || null,
          comment: null,
        }),
      }),
    onSuccess: async () => {
      setFlash('Начисление создано')
      setPayOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['payroll'] })
      await queryClient.invalidateQueries({ queryKey: ['payroll-summary'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const payAction = useMutation({
    mutationFn: async (payload: { id: string; action: 'approve' | 'paid' }) =>
      apiFetch(`/api/employees/payroll/${payload.id}/${payload.action}`, { method: 'POST' }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['payroll'] })
      await queryClient.invalidateQueries({ queryKey: ['payroll-summary'] })
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  const shellAdminStatus = useQuery({
    queryKey: ['shell-admin'],
    queryFn: async () => (await apiFetch<{ isConfigured: boolean }>('/api/settings/shell-admin')).data,
    enabled: isOwner,
  })

  const loginBgQuery = useQuery({
    queryKey: ['login-background'],
    queryFn: async () => (await apiFetch<{ url?: string | null }>('/api/settings/login-background')).data,
    enabled: isOwner,
  })

  const uploadLoginBgMutation = useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData()
      form.append('file', file)
      const res = await apiUpload<{ url?: string | null }>('/api/settings/login-background', form)
      if (!res.success) throw new Error(res.message || 'Не удалось загрузить')
      return res.data
    },
    onSuccess: () => {
      setLoginBgMsg('Фон экрана входа сохранён')
      void queryClient.invalidateQueries({ queryKey: ['login-background'] })
    },
    onError: (err) => setLoginBgMsg(err instanceof Error ? err.message : 'Ошибка загрузки'),
  })

  const clearLoginBgMutation = useMutation({
    mutationFn: async () => {
      const res = await apiFetch('/api/settings/login-background', { method: 'DELETE' })
      if (!res.success) throw new Error(res.message || 'Не удалось удалить')
    },
    onSuccess: () => {
      setLoginBgMsg('Фон сброшен — будет градиент по умолчанию')
      void queryClient.invalidateQueries({ queryKey: ['login-background'] })
    },
    onError: (err) => setLoginBgMsg(err instanceof Error ? err.message : 'Ошибка'),
  })

  const setShellAdminMutation = useMutation({
    mutationFn: async () => {
      if (shellAdminPwd.trim().length < 6) throw new Error('Минимум 6 символов')
      if (shellAdminPwd !== shellAdminPwd2) throw new Error('Пароли не совпадают')
      const res = await apiFetch('/api/settings/shell-admin-password', {
        method: 'PUT',
        body: JSON.stringify({ password: shellAdminPwd }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сохранить')
    },
    onSuccess: () => {
      setShellAdminPwd('')
      setShellAdminPwd2('')
      setShellAdminMsg('Пароль админ-режима Shell сохранён')
      queryClient.invalidateQueries({ queryKey: ['shell-admin'] })
    },
    onError: (err) => setShellAdminMsg(err instanceof Error ? err.message : 'Ошибка'),
  })

  return (
    <div className="employees-page">
      <header className="page-head">
        <div>
          <h1>Сотрудники</h1>
          <p className="muted" style={{ margin: '6px 0 0' }}>
            Команда клуба, доступы и журнал действий. Портал смен сотрудников:{' '}
            <a href="/work" target="_blank" rel="noreferrer">shift-club.kz/work</a>
          </p>
        </div>
        <div className="page-head-meta">
          {tab === 'people' && (
            <button type="button" onClick={() => { setError(null); setCreateOpen(true) }}>
              + Сотрудник
            </button>
          )}
          {tab === 'shifts' && (
            <button type="button" onClick={() => { setError(null); setShiftOpen(true) }}>
              + Смена
            </button>
          )}
          {tab === 'payroll' && (
            <button type="button" onClick={() => { setError(null); setPayOpen(true) }}>
              + Корректировка
            </button>
          )}
        </div>
      </header>

      <div className="emp-stats">
        <div className="emp-stat"><strong>{staffStats.total}</strong><span>всего</span></div>
        <div className="emp-stat"><strong>{staffStats.active}</strong><span>активны</span></div>
        <div className="emp-stat"><strong>{staffStats.shifts}</strong><span>смен закрыто</span></div>
        <div className="emp-stat"><strong>{staffStats.hours}</strong><span>часов отработано</span></div>
      </div>

      {employeesQuery.isError && <p className="error">{(employeesQuery.error as Error).message}</p>}
      {flash && <p className="flash">{flash}</p>}
      {error && tab !== 'people' && <p className="error">{error}</p>}

      <nav className="emp-tabs" aria-label="Разделы сотрудников">
        {(
          [
            ['people', 'Команда'],
            ['journal', 'Журнал'],
            ['shifts', 'График'],
            ['payroll', 'Зарплаты'],
            ['roles', 'Роли'],
            ...(isOwner ? ([['system', 'Система']] as const) : []),
          ] as const
        ).map(([id, label]) => (
          <button
            key={id}
            type="button"
            className={tab === id ? 'is-active' : undefined}
            onClick={() => { setTab(id); setError(null); setFlash(null) }}
          >
            {label}
          </button>
        ))}
      </nav>

      {tab === 'people' && (
        <div className="list-page">
          <div className="emp-toolbar">
            <input
              className="emp-search"
              placeholder="Поиск: имя, логин, телефон, роль…"
              value={peopleQ}
              onChange={(e) => setPeopleQ(e.target.value)}
            />
            <div className="emp-filter-pills">
              {(
                [
                  ['all', 'Все'],
                  ['active', 'Активные'],
                  ['off', 'Отключены'],
                ] as const
              ).map(([id, label]) => (
                <button
                  key={id}
                  type="button"
                  className={peopleFilter === id ? undefined : 'ghost'}
                  onClick={() => setPeopleFilter(id)}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
          <div className="emp-grid">
            {filteredStaff.map((e) => {
              const stats = summaryByEmployee.get(e.id)
              return (
              <article key={e.id} className={`emp-card${e.isActive ? '' : ' is-off'}`}>
                <div className="emp-card__top">
                  <div className="emp-avatar" aria-hidden>{initials(e.displayName)}</div>
                  <div className="emp-card__who">
                    <h2>{e.displayName}</h2>
                    <p>
                      @{e.login}
                      {e.phone ? ` · ${e.phone}` : ''}
                    </p>
                  </div>
                  <span className={`chip${e.isActive ? ' chip--ok' : ''}`}>{activeFlagLabel(e.isActive)}</span>
                </div>
                <div className="emp-card__meta">
                  <span className="emp-role-pill">{rolesLabel(e.roles)}</span>
                  <span>{employeePayTypeLabel(e.payType)}</span>
                  {e.hourlyRate > 0 && <span>{e.hourlyRate.toLocaleString('ru-RU')} ₸/ч</span>}
                  {e.monthlySalary > 0 && <span>оклад {e.monthlySalary.toLocaleString('ru-RU')}</span>}
                  {e.shiftRate > 0 && <span>смена {e.shiftRate.toLocaleString('ru-RU')}</span>}
                </div>
                {(stats?.shiftsCompleted ?? 0) > 0 || (stats?.workedHours ?? 0) > 0 ? (
                  <div className="emp-card__meta">
                    <span>{stats?.shiftsCompleted ?? 0} смен</span>
                    <span>{stats?.workedHours ?? 0} ч</span>
                    {(stats?.remaining ?? 0) > 0 && <span>к выплате {stats?.remaining.toLocaleString('ru-RU')} ₸</span>}
                  </div>
                ) : (
                  <p className="muted emp-card__login">Смен пока нет</p>
                )}
                {e.lastLoginAt && (
                  <p className="muted emp-card__login">
                    Последний вход: {new Date(e.lastLoginAt).toLocaleString('ru-RU')}
                  </p>
                )}
                <div className="emp-card__actions">
                  <button type="button" className="ghost" onClick={() => openEdit(e)}>Изменить</button>
                  <button type="button" className="ghost" onClick={() => openPassword(e)}>Пароль</button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={toggleActiveMutation.isPending}
                    onClick={() => {
                      if (e.isActive && !confirm(`Отключить «${e.displayName}»?`)) return
                      toggleActiveMutation.mutate(e)
                    }}
                  >
                    {e.isActive ? 'Отключить' : 'Включить'}
                  </button>
                  <button
                    type="button"
                    className="ghost"
                    disabled={deleteMutation.isPending}
                    onClick={() => {
                      if (!confirm(`Удалить аккаунт «${e.displayName}» (${e.login})? Войти будет нельзя.`)) return
                      deleteMutation.mutate(e)
                    }}
                  >
                    Удалить
                  </button>
                </div>
              </article>
            )})}
            {filteredStaff.length === 0 && (
              <p className="muted" style={{ gridColumn: '1 / -1', padding: 24, textAlign: 'center' }}>
                Никого не найдено
              </p>
            )}
          </div>
        </div>
      )}

      {tab === 'journal' && (
        <div className="list-page">
          <div className="emp-journal-filters">
            <label>
              С
              <input type="date" value={journalFrom} onChange={(e) => { setJournalFrom(e.target.value); setJournalPage(1) }} />
            </label>
            <label>
              По
              <input type="date" value={journalTo} onChange={(e) => { setJournalTo(e.target.value); setJournalPage(1) }} />
            </label>
            <label>
              Сотрудник
              <select value={journalEmployeeId} onChange={(e) => { setJournalEmployeeId(e.target.value); setJournalPage(1) }}>
                <option value="">Все</option>
                {staff.map((e) => (
                  <option key={e.id} value={e.id}>{e.displayName}</option>
                ))}
              </select>
            </label>
            <label>
              Действие
              <select value={journalAction} onChange={(e) => { setJournalAction(e.target.value); setJournalPage(1) }}>
                <option value="">Все типы</option>
                {(auditActionsQuery.data ?? []).map((a) => (
                  <option key={a} value={a}>{auditActionLabel(a)}</option>
                ))}
              </select>
            </label>
            <label className="emp-journal-search">
              Поиск
              <input
                placeholder="логин, entity, IP…"
                value={journalSearch}
                onChange={(e) => { setJournalSearch(e.target.value); setJournalPage(1) }}
              />
            </label>
            <label className="emp-tech-toggle">
              <input
                type="checkbox"
                checked={journalTechnical}
                onChange={(e) => { setJournalTechnical(e.target.checked); setJournalPage(1) }}
              />
              Технические (команды ПК / WoL)
            </label>
          </div>
          {journalQuery.isError && <p className="error">{(journalQuery.error as Error).message}</p>}
          {journalQuery.isFetching && !journalQuery.data && <p className="muted">Загрузка журнала…</p>}
          <div className="emp-journal-list">
            {(journalQuery.data?.items ?? []).map((row) => {
              const tone = auditActionTone(row.action)
              return (
                <article key={row.id} className={`emp-journal-row tone-${tone}`}>
                  <div className="emp-journal-row__when">
                    <strong>{new Date(row.createdAt).toLocaleString('ru-RU')}</strong>
                    {row.ipAddress && <span className="muted">{row.ipAddress}</span>}
                  </div>
                  <div className="emp-journal-row__who">
                    <strong>{row.employeeName ?? 'Система'}</strong>
                    {row.employeeLogin && <span className="muted">@{row.employeeLogin}</span>}
                  </div>
                  <div className="emp-journal-row__what">
                    <span className={`emp-action-chip tone-${tone}`}>{auditActionLabel(row.action)}</span>
                    <span className="muted">
                      {row.entityType}
                      {row.entityId ? ` · ${row.entityId.slice(0, 8)}…` : ''}
                    </span>
                    {detailsPreview(row.detailsJson) && (
                      <span className="emp-journal-details">{detailsPreview(row.detailsJson)}</span>
                    )}
                  </div>
                </article>
              )
            })}
            {(journalQuery.data?.items.length ?? 0) === 0 && !journalQuery.isFetching && (
              <p className="muted" style={{ padding: 24, textAlign: 'center' }}>Записей нет за выбранный период</p>
            )}
          </div>
          {(journalQuery.data?.total ?? 0) > (journalQuery.data?.pageSize ?? 40) && (
            <div className="emp-pager">
              <button type="button" className="ghost" disabled={journalPage <= 1} onClick={() => setJournalPage((p) => Math.max(1, p - 1))}>
                ← Назад
              </button>
              <span className="muted">
                стр. {journalQuery.data?.page ?? journalPage} · всего {journalQuery.data?.total ?? 0}
              </span>
              <button
                type="button"
                className="ghost"
                disabled={(journalQuery.data?.page ?? 1) * (journalQuery.data?.pageSize ?? 40) >= (journalQuery.data?.total ?? 0)}
                onClick={() => setJournalPage((p) => p + 1)}
              >
                Вперёд →
              </button>
            </div>
          )}
        </div>
      )}

      {tab === 'shifts' && (
        <div className="list-page">
          <section className="list-card">
            <p className="muted" style={{ padding: '12px 16px 0', margin: 0 }}>
              Назначайте смены здесь или в портале{' '}
              <a href="/work" target="_blank" rel="noreferrer">/work</a>
              . Сотрудники отмечают приход/уход сами.
            </p>
            <div className="branch-list" style={{ margin: '8px 4px 12px' }}>
              {(shiftsQuery.data ?? []).map((s) => (
                <article key={s.id} className="branch-block">
                  <header>
                    <h2>{s.employeeName} · {s.workDate}</h2>
                    <span className="chip">{workShiftStatusLabel(s.status)}</span>
                  </header>
                  <p className="muted">
                    план {(() => {
                      const a = String(s.plannedStart).slice(0, 5)
                      const b = String(s.plannedEnd).slice(0, 5)
                      const overnight = b < a
                      return overnight ? `${a}–${b} (+1 день)` : `${a}–${b}`
                    })()}
                    {s.actualStartAt && ` · приход ${new Date(s.actualStartAt).toLocaleString('ru-RU', { timeZone: 'Asia/Almaty', hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' })}`}
                    {s.actualEndAt && ` · уход ${new Date(s.actualEndAt).toLocaleString('ru-RU', { timeZone: 'Asia/Almaty', hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' })}`}
                    {s.workedHours > 0 && ` · факт ${s.workedHours} ч`}
                    {s.breakMinutes > 0 && ` · перерыв ${s.breakMinutes} мин`}
                    {s.comment ? ` · ${s.comment}` : ''}
                  </p>
                  <div className="action-chip-row">
                    {['Scheduled', 'Late'].includes(workShiftStatusName(s.status) ?? '') && (
                      <button type="button" onClick={() => shiftAction.mutate({ id: s.id, action: 'clock-in' })}>Приход</button>
                    )}
                    {['Working', 'Late'].includes(workShiftStatusName(s.status) ?? '') && (
                      <button type="button" className="ghost" onClick={() => shiftAction.mutate({ id: s.id, action: 'break-start' })}>Перерыв</button>
                    )}
                    {workShiftStatusName(s.status) === 'OnBreak' && (
                      <button type="button" onClick={() => shiftAction.mutate({ id: s.id, action: 'break-end' })}>С перерыва</button>
                    )}
                    {['Working', 'Late', 'OnBreak'].includes(workShiftStatusName(s.status) ?? '') && (
                      <button type="button" onClick={() => shiftAction.mutate({ id: s.id, action: 'clock-out' })}>Уход</button>
                    )}
                    {workShiftStatusName(s.status) === 'Scheduled' && (
                      <button type="button" className="ghost" onClick={() => shiftAction.mutate({ id: s.id, action: 'absent' })}>Неявка</button>
                    )}
                    {['Scheduled', 'Absent'].includes(workShiftStatusName(s.status) ?? '') && (
                      <button type="button" className="ghost" onClick={() => shiftAction.mutate({ id: s.id, action: 'cancel' })}>Отменить</button>
                    )}
                  </div>
                </article>
              ))}
              {(shiftsQuery.data?.length ?? 0) === 0 && <p className="muted" style={{ padding: 16 }}>Смен пока нет — назначьте первую.</p>}
            </div>
          </section>
        </div>
      )}

      {tab === 'payroll' && (
        <div className="list-page">
          <section className="list-card">
            <div className="branch-list" style={{ margin: '8px 4px 12px' }}>
              {(summaryQuery.data ?? []).map((s) => (
                <article key={s.employeeId} className="branch-block">
                  <header>
                    <h2>{s.employeeName}</h2>
                    <span className="chip">{s.remaining.toLocaleString('ru-RU')} ₸</span>
                  </header>
                  <p className="muted">
                    система насчитала {s.accrued.toLocaleString('ru-RU')} · выплачено {s.paid.toLocaleString('ru-RU')} · {s.workedHours} ч · {s.shiftsCompleted} смен
                  </p>
                </article>
              ))}
            </div>
            <h3 style={{ padding: '8px 12px 0' }}>Ручные корректировки</h3>
            <div className="branch-list" style={{ margin: '8px 4px 12px' }}>
              {(payrollQuery.data ?? []).map((a) => (
                <article key={a.id} className="branch-block">
                  <header>
                    <h2>{a.employeeName} · {a.amount.toLocaleString('ru-RU')} ₸</h2>
                    <span className="chip">{payrollStatusLabel(a.status)}</span>
                  </header>
                  <p className="muted">
                    {payrollTypeLabel(a.type)} · {a.periodFrom}…{a.periodTo}
                    {a.basis ? ` · ${a.basis}` : ''}
                  </p>
                  <div className="action-chip-row">
                    {payrollStatusName(a.status) === 'Draft' && (
                      <button type="button" onClick={() => payAction.mutate({ id: a.id, action: 'approve' })}>Утвердить</button>
                    )}
                    {['Draft', 'Approved'].includes(payrollStatusName(a.status) ?? '') && (
                      <button type="button" onClick={() => payAction.mutate({ id: a.id, action: 'paid' })}>Выплачено</button>
                    )}
                  </div>
                </article>
              ))}
            </div>
          </section>
        </div>
      )}

      {tab === 'roles' && (
        <section className="list-card">
          <p className="muted" style={{ padding: '12px 16px 0', margin: 0 }}>
            Системные роли. Набор прав кассира синхронизируется при старте API.
          </p>
          <div className="emp-roles-grid">
            {(rolesQuery.data ?? []).map((r) => (
              <article key={r.id} className="emp-role-card">
                <header>
                  <h2>{r.name}</h2>
                  <span className="chip">{r.permissions.length} прав</span>
                </header>
                <p className="muted"><code>{r.code}</code>{r.description ? ` · ${r.description}` : ''}</p>
                <div className="emp-perm-cloud">
                  {r.permissions.map((p) => (
                    <span key={p} className="emp-perm">{p}</span>
                  ))}
                </div>
              </article>
            ))}
          </div>
        </section>
      )}

      {tab === 'system' && isOwner && (
        <div className="emp-system">
          <section className="list-card">
            <h2 className="section-title">Telegram-бот</h2>
            <p className="muted" style={{ marginTop: 0 }}>Токен, allow-list сотрудников, алерты в чат клуба.</p>
            <Link to="/telegram" className="ghost" style={{ display: 'inline-block', marginTop: 8, padding: '10px 14px' }}>
              Открыть настройки Telegram →
            </Link>
          </section>
          <section className="list-card">
            <h2 className="section-title">Админ-режим Shell</h2>
            <p className="muted" style={{ marginTop: 0 }}>
              Пароль кнопки «Админ» на экране входа клиента (настройка образа CCBoot).
              {shellAdminStatus.data?.isConfigured ? ' Сейчас пароль задан.' : ' Пароль ещё не задан.'}
            </p>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'flex-end', marginTop: 12 }}>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                <span className="muted" style={{ fontSize: 12 }}>Новый пароль</span>
                <input type="password" value={shellAdminPwd} onChange={(e) => { setShellAdminPwd(e.target.value); setShellAdminMsg(null) }} autoComplete="new-password" style={{ minWidth: 180 }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
                <span className="muted" style={{ fontSize: 12 }}>Повтор</span>
                <input type="password" value={shellAdminPwd2} onChange={(e) => { setShellAdminPwd2(e.target.value); setShellAdminMsg(null) }} autoComplete="new-password" style={{ minWidth: 180 }} />
              </label>
              <button type="button" disabled={setShellAdminMutation.isPending} onClick={() => setShellAdminMutation.mutate()}>Сохранить</button>
            </div>
            {shellAdminMsg && (
              <p className={shellAdminMsg.includes('сохранён') ? 'flash' : 'error'} style={{ marginTop: 10 }}>{shellAdminMsg}</p>
            )}
          </section>
          <section className="list-card">
            <h2 className="section-title">Фон экрана входа</h2>
            <p className="muted" style={{ marginTop: 0 }}>
              Картинка на login Shell / панели.
              {loginBgQuery.data?.url ? ' Сейчас загружен свой фон.' : ' Сейчас градиент по умолчанию.'}
            </p>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, alignItems: 'center', marginTop: 8 }}>
              <label className="ghost" style={{ cursor: 'pointer', padding: '10px 14px', display: 'inline-block' }}>
                {uploadLoginBgMutation.isPending ? 'Загрузка…' : 'Выбрать картинку'}
                <input
                  type="file"
                  accept="image/jpeg,image/png,image/webp,image/gif"
                  hidden
                  disabled={uploadLoginBgMutation.isPending}
                  onChange={(e) => {
                    const f = e.target.files?.[0]
                    e.target.value = ''
                    if (!f) return
                    setLoginBgMsg(null)
                    uploadLoginBgMutation.mutate(f)
                  }}
                />
              </label>
              {loginBgQuery.data?.url && (
                <button type="button" className="ghost" disabled={clearLoginBgMutation.isPending} onClick={() => { setLoginBgMsg(null); clearLoginBgMutation.mutate() }}>
                  Сбросить
                </button>
              )}
            </div>
            {loginBgMsg && (
              <p className={loginBgMsg.includes('сохран') || loginBgMsg.includes('сброшен') ? 'flash' : 'error'} style={{ marginTop: 10 }}>{loginBgMsg}</p>
            )}
          </section>
        </div>
      )}

      <ActionDialog
        open={createOpen}
        onClose={() => !createMutation.isPending && setCreateOpen(false)}
        title="Новый сотрудник"
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setCreateOpen(false)} disabled={createMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={createMutation.isPending || !createForm.login || !createForm.displayName} onClick={() => createMutation.mutate()}>
              Создать
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="field-grid">
            <label>
              Логин
              <input value={createForm.login} onChange={(e) => setCreateForm((f) => ({ ...f, login: e.target.value }))} />
            </label>
            <label>
              Имя
              <input value={createForm.displayName} onChange={(e) => setCreateForm((f) => ({ ...f, displayName: e.target.value }))} />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Пароль
              <input value={createForm.password} onChange={(e) => setCreateForm((f) => ({ ...f, password: e.target.value }))} />
            </label>
            <label>
              Телефон
              <input value={createForm.phone} onChange={(e) => setCreateForm((f) => ({ ...f, phone: e.target.value }))} />
            </label>
          </div>
          <label>
            Роли
            <div className="action-chip-row" style={{ marginTop: 8 }}>
              {(rolesQuery.data ?? []).map((r) => {
                const on = createForm.roleCodes.includes(r.code)
                return (
                  <button
                    key={r.code}
                    type="button"
                    className={on ? undefined : 'ghost'}
                    onClick={() =>
                      toggleRole(r.code, createForm.roleCodes, (roleCodes) => setCreateForm((f) => ({ ...f, roleCodes })))
                    }
                  >
                    {r.name}
                  </button>
                )
              })}
            </div>
          </label>
          <div className="field-grid">
            <label>
              Тип оплаты
              <select value={createForm.payType} onChange={(e) => setCreateForm((f) => ({ ...f, payType: e.target.value }))}>
                <option value="Hourly">Почасовая</option>
                <option value="FixedMonthly">Оклад</option>
                <option value="PerShift">За смену</option>
              </select>
            </label>
            <label>
              Ставка ₸/ч
              <input
                type="number"
                value={createForm.hourlyRate}
                onChange={(e) => setCreateForm((f) => ({ ...f, hourlyRate: Number(e.target.value) }))}
              />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Оклад
              <input
                type="number"
                value={createForm.monthlySalary}
                onChange={(e) => setCreateForm((f) => ({ ...f, monthlySalary: Number(e.target.value) }))}
              />
            </label>
            <label>
              Ставка за смену
              <input
                type="number"
                value={createForm.shiftRate}
                onChange={(e) => setCreateForm((f) => ({ ...f, shiftRate: Number(e.target.value) }))}
              />
            </label>
          </div>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={editOpen && !!editForm}
        onClose={() => !updateMutation.isPending && setEditOpen(false)}
        title={editForm ? `Сотрудник · ${editForm.login}` : 'Сотрудник'}
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setEditOpen(false)} disabled={updateMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={updateMutation.isPending || !editForm?.displayName.trim() || (editForm?.roleCodes.length ?? 0) === 0}
              onClick={() => editForm && updateMutation.mutate(editForm)}
            >
              Сохранить
            </button>
          </>
        }
      >
        {editForm && (
          <div className="form-stack">
            <p className="muted" style={{ margin: 0 }}>
              Логин нельзя сменить: <strong>{editForm.login}</strong>
            </p>
            <label>
              Имя
              <input
                value={editForm.displayName}
                onChange={(e) => setEditForm((f) => (f ? { ...f, displayName: e.target.value } : f))}
              />
            </label>
            <div className="field-grid">
              <label>
                Телефон
                <input
                  value={editForm.phone}
                  onChange={(e) => setEditForm((f) => (f ? { ...f, phone: e.target.value } : f))}
                />
              </label>
              <label>
                Email
                <input
                  value={editForm.email}
                  onChange={(e) => setEditForm((f) => (f ? { ...f, email: e.target.value } : f))}
                />
              </label>
            </div>
            <label>
              Роли
              <div className="action-chip-row" style={{ marginTop: 8 }}>
                {(rolesQuery.data ?? []).map((r) => {
                  const on = editForm.roleCodes.includes(r.code)
                  return (
                    <button
                      key={r.code}
                      type="button"
                      className={on ? undefined : 'ghost'}
                      onClick={() =>
                        toggleRole(r.code, editForm.roleCodes, (roleCodes) =>
                          setEditForm((f) => (f ? { ...f, roleCodes } : f)),
                        )
                      }
                    >
                      {r.name}
                    </button>
                  )
                })}
              </div>
            </label>
            <div className="field-grid">
              <label>
                Тип оплаты
                <select
                  value={editForm.payType}
                  onChange={(e) => setEditForm((f) => (f ? { ...f, payType: e.target.value } : f))}
                >
                  <option value="Hourly">Почасовая</option>
                  <option value="FixedMonthly">Оклад</option>
                  <option value="PerShift">За смену</option>
                </select>
              </label>
              <label>
                Активен
                <select
                  value={editForm.isActive ? '1' : '0'}
                  onChange={(e) => setEditForm((f) => (f ? { ...f, isActive: e.target.value === '1' } : f))}
                >
                  <option value="1">Да</option>
                  <option value="0">Нет</option>
                </select>
              </label>
            </div>
            <div className="field-grid">
              <label>
                Ставка ₸/ч
                <input
                  type="number"
                  value={editForm.hourlyRate}
                  onChange={(e) => setEditForm((f) => (f ? { ...f, hourlyRate: Number(e.target.value) } : f))}
                />
              </label>
              <label>
                Оклад
                <input
                  type="number"
                  value={editForm.monthlySalary}
                  onChange={(e) => setEditForm((f) => (f ? { ...f, monthlySalary: Number(e.target.value) } : f))}
                />
              </label>
            </div>
            <label>
              Ставка за смену
              <input
                type="number"
                value={editForm.shiftRate}
                onChange={(e) => setEditForm((f) => (f ? { ...f, shiftRate: Number(e.target.value) } : f))}
              />
            </label>
            {error && <p className="error">{error}</p>}
          </div>
        )}
      </ActionDialog>

      <ActionDialog
        open={passwordOpen}
        onClose={() => !passwordMutation.isPending && setPasswordOpen(false)}
        title={passwordForm.name ? `Пароль · ${passwordForm.name}` : 'Пароль'}
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setPasswordOpen(false)} disabled={passwordMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={passwordMutation.isPending} onClick={() => passwordMutation.mutate()}>
              Сменить
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Новый пароль
            <input
              type="password"
              value={passwordForm.password}
              onChange={(e) => setPasswordForm((f) => ({ ...f, password: e.target.value }))}
              autoComplete="new-password"
            />
          </label>
          <label>
            Повтор
            <input
              type="password"
              value={passwordForm.password2}
              onChange={(e) => setPasswordForm((f) => ({ ...f, password2: e.target.value }))}
              autoComplete="new-password"
            />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={shiftOpen}
        onClose={() => !shiftMutation.isPending && setShiftOpen(false)}
        title="Назначить смену"
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setShiftOpen(false)} disabled={shiftMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={shiftMutation.isPending || !selectedEmployeeId} onClick={() => shiftMutation.mutate()}>
              Добавить
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Сотрудник
            <select
              value={selectedEmployeeId}
              onChange={(e) => setShiftForm((f) => ({ ...f, employeeId: e.target.value }))}
            >
              {staff.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Дата
            <input type="date" value={shiftForm.workDate} onChange={(e) => setShiftForm((f) => ({ ...f, workDate: e.target.value }))} />
          </label>
          <div className="field-grid">
            <label>
              Начало
              <input type="time" value={shiftForm.plannedStart} onChange={(e) => setShiftForm((f) => ({ ...f, plannedStart: e.target.value }))} />
            </label>
            <label>
              Конец
              <input type="time" value={shiftForm.plannedEnd} onChange={(e) => setShiftForm((f) => ({ ...f, plannedEnd: e.target.value }))} />
            </label>
          </div>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={payOpen}
        onClose={() => !payMutation.isPending && setPayOpen(false)}
        title="Ручная корректировка"
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setPayOpen(false)} disabled={payMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={payMutation.isPending} onClick={() => payMutation.mutate()}>
              Создать
            </button>
          </>
        }
      >
        <div className="form-stack">
          <p className="muted" style={{ margin: 0 }}>
            Базовую оплату система считает сама по часам / сменам / окладу. Здесь добавляйте только бонусы, штрафы, авансы и другие корректировки.
          </p>
          <label>
            Сотрудник
            <select
              value={payForm.employeeId || staff[0]?.id || ''}
              onChange={(e) => setPayForm((f) => ({ ...f, employeeId: e.target.value }))}
            >
              {staff.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.displayName}
                </option>
              ))}
            </select>
          </label>
          <div className="field-grid">
            <label>
              Тип
              <select value={payForm.type} onChange={(e) => setPayForm((f) => ({ ...f, type: e.target.value }))}>
                <option value="Salary">Оклад</option>
                <option value="Hourly">Почасовая</option>
                <option value="ShiftPay">За смену</option>
                <option value="Bonus">Бонус</option>
                <option value="Penalty">Штраф</option>
                <option value="Advance">Аванс</option>
                <option value="Premium">Премия</option>
              </select>
            </label>
            <label>
              Сумма
              <input type="number" value={payForm.amount} onChange={(e) => setPayForm((f) => ({ ...f, amount: Number(e.target.value) }))} />
            </label>
          </div>
          <label>
            Основание
            <input value={payForm.basis} onChange={(e) => setPayForm((f) => ({ ...f, basis: e.target.value }))} />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>
    </div>
  )
}
