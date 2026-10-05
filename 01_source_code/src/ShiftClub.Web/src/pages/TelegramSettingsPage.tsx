import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { apiFetch, type EmployeeDto } from '../api/client'

type AllowedUser = {
  telegramUserId: number
  employeeId: string | null
  displayName: string | null
  receiveAlerts: boolean
}

type TelegramSettings = {
  enabled: boolean
  hasToken: boolean
  botTokenMasked: string | null
  botUsername: string | null
  allowedUsers: AllowedUser[]
  alertChatIds: number[]
  notifyHelp: boolean
  notifySecurity: boolean
  notifyBar: boolean
  notifySessionWarning: boolean
  notifyBooking: boolean
  defaultEmployeeId: string | null
  runtimeStatus: string
  runtimeDetail: string | null
  publicWebAppBaseUrl?: string | null
}

type EmployeeItem = {
  id: string
  login: string
  displayName: string
  isActive: boolean
}

type TgUsersAdmin = {
  customersLinked: number
  employeesLinked: number
  allowlistCount: number
  customers: {
    id: string
    fullName: string
    phone: string
    telegramUserId: number
    linkedAt: string | null
    isActive: boolean
  }[]
  employees: {
    id: string
    login: string
    displayName: string
    phone: string | null
    telegramUserId: number | null
    linkedAt: string | null
    inAllowlist: boolean
    isActive: boolean
  }[]
}

type SectionId = 'setup' | 'access' | 'people' | 'alerts' | 'crm' | 'broadcast'

type TelegramCrmSettings = {
  enabled: boolean
  quietHourFrom: number
  quietHourTo: number
  minDaysBetweenMessages: number
  maxSendsPerTick: number
  maxSendsPerDay: number
  winbackEnabled: boolean
  winbackAfterDays: number
  winbackCooldownDays: number
  unusedKeyEnabled: boolean
  unusedKeyMinIdleDays: number
  unusedKeyCooldownDays: number
  promoNudgeEnabled: boolean
  promoNudgeCooldownDays: number
  stats: {
    linkedCustomers: number
    sentToday: number
    winbackCandidates: number
    unusedKeyCandidates: number
    promoCandidates: number
  }
}

const emptyCrmForm = (): Omit<TelegramCrmSettings, 'stats'> => ({
  enabled: true,
  quietHourFrom: 11,
  quietHourTo: 21,
  minDaysBetweenMessages: 5,
  maxSendsPerTick: 20,
  maxSendsPerDay: 60,
  winbackEnabled: true,
  winbackAfterDays: 10,
  winbackCooldownDays: 21,
  unusedKeyEnabled: true,
  unusedKeyMinIdleDays: 3,
  unusedKeyCooldownDays: 14,
  promoNudgeEnabled: true,
  promoNudgeCooldownDays: 10,
})

const emptyForm = (): Omit<
  TelegramSettings,
  'hasToken' | 'botTokenMasked' | 'botUsername' | 'runtimeStatus' | 'runtimeDetail'
> & { botToken: string } => ({
  enabled: false,
  botToken: '',
  allowedUsers: [],
  alertChatIds: [],
  notifyHelp: true,
  notifySecurity: true,
  notifyBar: true,
  notifySessionWarning: true,
  notifyBooking: true,
  defaultEmployeeId: null,
  publicWebAppBaseUrl: '',
})

const SECTIONS: { id: SectionId; label: string }[] = [
  { id: 'setup', label: 'Бот' },
  { id: 'access', label: 'Доступ' },
  { id: 'people', label: 'Люди' },
  { id: 'alerts', label: 'Алерты' },
  { id: 'crm', label: 'Авто' },
  { id: 'broadcast', label: 'Рассылка' },
]

export function TelegramSettingsPage() {
  const queryClient = useQueryClient()
  const me = JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as EmployeeDto | null
  const isOwner = (me?.roles ?? []).some((r) => String(r).toLowerCase() === 'owner')

  const [section, setSection] = useState<SectionId>('setup')
  const [form, setForm] = useState(emptyForm)
  const [alertChatsText, setAlertChatsText] = useState('')
  const [msg, setMsg] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [newUserId, setNewUserId] = useState('')
  const [newUserName, setNewUserName] = useState('')
  const [newUserEmployee, setNewUserEmployee] = useState('')
  const [broadcastAudience, setBroadcastAudience] = useState<'customers' | 'staff' | 'both'>('customers')
  const [broadcastText, setBroadcastText] = useState('')
  const [broadcastMsg, setBroadcastMsg] = useState<string | null>(null)
  const [broadcastErr, setBroadcastErr] = useState<string | null>(null)
  const [makeStaffCustomerId, setMakeStaffCustomerId] = useState('')
  const [makeStaffEmployeeId, setMakeStaffEmployeeId] = useState('')
  const [usersMsg, setUsersMsg] = useState<string | null>(null)
  const [usersErr, setUsersErr] = useState<string | null>(null)
  const [peopleTab, setPeopleTab] = useState<'customers' | 'staff'>('customers')
  const [crmForm, setCrmForm] = useState(emptyCrmForm)
  const [crmMsg, setCrmMsg] = useState<string | null>(null)
  const [crmErr, setCrmErr] = useState<string | null>(null)

  const settingsQuery = useQuery({
    queryKey: ['telegram-settings'],
    queryFn: async () => (await apiFetch<TelegramSettings>('/api/settings/telegram')).data!,
    enabled: isOwner,
    refetchInterval: 5000,
  })

  const crmQuery = useQuery({
    queryKey: ['telegram-crm'],
    queryFn: async () => (await apiFetch<TelegramCrmSettings>('/api/settings/telegram/crm')).data!,
    enabled: isOwner,
    refetchInterval: 30000,
  })

  const usersQuery = useQuery({
    queryKey: ['telegram-users'],
    queryFn: async () => (await apiFetch<TgUsersAdmin>('/api/settings/telegram/users')).data!,
    enabled: isOwner,
    refetchInterval: 10000,
  })

  const employeesQuery = useQuery({
    queryKey: ['employees-list'],
    queryFn: async () => (await apiFetch<EmployeeItem[]>('/api/employees')).data ?? [],
    enabled: isOwner,
  })

  useEffect(() => {
    const d = settingsQuery.data
    if (!d) return
    setForm({
      enabled: d.enabled,
      botToken: '',
      allowedUsers: d.allowedUsers ?? [],
      alertChatIds: d.alertChatIds ?? [],
      notifyHelp: d.notifyHelp,
      notifySecurity: d.notifySecurity,
      notifyBar: d.notifyBar,
      notifySessionWarning: d.notifySessionWarning,
      notifyBooking: d.notifyBooking,
      defaultEmployeeId: d.defaultEmployeeId,
      publicWebAppBaseUrl: d.publicWebAppBaseUrl ?? '',
    })
    setAlertChatsText((d.alertChatIds ?? []).join(', '))
  }, [settingsQuery.data])

  useEffect(() => {
    const d = crmQuery.data
    if (!d) return
    setCrmForm({
      enabled: d.enabled,
      quietHourFrom: d.quietHourFrom,
      quietHourTo: d.quietHourTo,
      minDaysBetweenMessages: d.minDaysBetweenMessages,
      maxSendsPerTick: d.maxSendsPerTick,
      maxSendsPerDay: d.maxSendsPerDay,
      winbackEnabled: d.winbackEnabled,
      winbackAfterDays: d.winbackAfterDays,
      winbackCooldownDays: d.winbackCooldownDays,
      unusedKeyEnabled: d.unusedKeyEnabled,
      unusedKeyMinIdleDays: d.unusedKeyMinIdleDays,
      unusedKeyCooldownDays: d.unusedKeyCooldownDays,
      promoNudgeEnabled: d.promoNudgeEnabled,
      promoNudgeCooldownDays: d.promoNudgeCooldownDays,
    })
  }, [crmQuery.data])

  const saveMutation = useMutation({
    mutationFn: async () => {
      const chats = alertChatsText
        .split(/[,\s;]+/)
        .map((s) => s.trim())
        .filter(Boolean)
        .map((s) => Number(s))
        .filter((n) => Number.isFinite(n) && n !== 0)

      const body = {
        enabled: form.enabled,
        botToken: form.botToken.trim().length > 0 ? form.botToken.trim() : null,
        allowedUsers: form.allowedUsers.map((u) => ({
          telegramUserId: u.telegramUserId,
          employeeId: u.employeeId,
          displayName: u.displayName,
          receiveAlerts: u.receiveAlerts,
        })),
        alertChatIds: chats,
        notifyHelp: form.notifyHelp,
        notifySecurity: form.notifySecurity,
        notifyBar: form.notifyBar,
        notifySessionWarning: form.notifySessionWarning,
        notifyBooking: form.notifyBooking,
        defaultEmployeeId: form.defaultEmployeeId,
        publicWebAppBaseUrl: form.publicWebAppBaseUrl?.trim() || '',
      }

      const res = await apiFetch<TelegramSettings>('/api/settings/telegram', {
        method: 'PUT',
        body: JSON.stringify(body),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сохранить')
      return res.data!
    },
    onSuccess: () => {
      setErr(null)
      setMsg('Сохранено. Бот перезапускается…')
      setForm((f) => ({ ...f, botToken: '' }))
      void queryClient.invalidateQueries({ queryKey: ['telegram-settings'] })
    },
    onError: (e) => {
      setMsg(null)
      setErr(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  const broadcastMutation = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<{
        queued: number
        customers: number
        staff: number
        preview: string
      }>('/api/settings/telegram/broadcast', {
        method: 'POST',
        body: JSON.stringify({
          audience: broadcastAudience,
          message: broadcastText,
        }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось отправить')
      return res.data!
    },
    onSuccess: (d) => {
      setBroadcastErr(null)
      setBroadcastMsg(`Отправлено в очередь: ${d.queued}`)
      setBroadcastText('')
    },
    onError: (e) => {
      setBroadcastMsg(null)
      setBroadcastErr(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  const unlinkCustomerMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiFetch(`/api/settings/telegram/users/customers/${id}/unlink`, { method: 'POST' })
    },
    onSuccess: () => {
      setUsersErr(null)
      setUsersMsg('Telegram отвязан у клиента')
      void queryClient.invalidateQueries({ queryKey: ['telegram-users'] })
    },
    onError: (e) => setUsersErr(e instanceof Error ? e.message : 'Ошибка'),
  })

  const unlinkEmployeeMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiFetch(`/api/settings/telegram/users/employees/${id}/unlink`, { method: 'POST' })
    },
    onSuccess: () => {
      setUsersErr(null)
      setUsersMsg('Telegram отвязан у сотрудника')
      void queryClient.invalidateQueries({ queryKey: ['telegram-users'] })
      void queryClient.invalidateQueries({ queryKey: ['telegram-settings'] })
    },
    onError: (e) => setUsersErr(e instanceof Error ? e.message : 'Ошибка'),
  })

  const makeStaffMutation = useMutation({
    mutationFn: async () => {
      if (!makeStaffEmployeeId || !makeStaffCustomerId) {
        throw new Error('Выберите клиента и сотрудника')
      }
      await apiFetch('/api/settings/telegram/users/make-staff', {
        method: 'POST',
        body: JSON.stringify({
          employeeId: makeStaffEmployeeId,
          customerId: makeStaffCustomerId,
        }),
      })
    },
    onSuccess: () => {
      setUsersErr(null)
      setUsersMsg('Готово: Telegram привязан к сотруднику и добавлен в доступ бота')
      setMakeStaffCustomerId('')
      setMakeStaffEmployeeId('')
      void queryClient.invalidateQueries({ queryKey: ['telegram-users'] })
      void queryClient.invalidateQueries({ queryKey: ['telegram-settings'] })
    },
    onError: (e) => setUsersErr(e instanceof Error ? e.message : 'Ошибка'),
  })

  const crmSaveMutation = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<TelegramCrmSettings>('/api/settings/telegram/crm', {
        method: 'PUT',
        body: JSON.stringify(crmForm),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сохранить')
      return res.data!
    },
    onSuccess: () => {
      setCrmErr(null)
      setCrmMsg('Автосообщения сохранены')
      void queryClient.invalidateQueries({ queryKey: ['telegram-crm'] })
    },
    onError: (e) => {
      setCrmMsg(null)
      setCrmErr(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  if (!isOwner) {
    return (
      <div className="tg-page">
        <header className="page-head">
          <div>
            <h1>Telegram</h1>
          </div>
        </header>
        <p className="muted">Настройки доступны только владельцу.</p>
      </div>
    )
  }

  const d = settingsQuery.data
  const status = d?.runtimeStatus ?? '…'
  const statusTone =
    status === 'running' ? 'ok' : status === 'error' ? 'bad' : status === 'starting' ? 'warn' : 'idle'

  function addUser() {
    const id = Number(newUserId.trim())
    if (!Number.isFinite(id) || id === 0) {
      setErr('Укажите числовой Telegram ID')
      return
    }
    setErr(null)
    setForm((f) => ({
      ...f,
      allowedUsers: [
        ...f.allowedUsers.filter((u) => u.telegramUserId !== id),
        {
          telegramUserId: id,
          employeeId: newUserEmployee || null,
          displayName: newUserName.trim() || null,
          receiveAlerts: true,
        },
      ],
    }))
    setNewUserId('')
    setNewUserName('')
    setNewUserEmployee('')
    setMsg('Добавлено в список — нажмите «Сохранить»')
  }

  const activeEmployees = (employeesQuery.data ?? []).filter((e) => e.isActive)

  return (
    <div className="tg-page">
      <header className="page-head tg-page__head">
        <div>
          <p className="tg-kicker">Интеграции</p>
          <h1>Telegram</h1>
          <p className="muted" style={{ margin: 0 }}>
            Бот смены, Mini App и рассылки — в одном месте.
          </p>
        </div>
        <div className="tg-head-actions">
          <button type="button" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate()}>
            {saveMutation.isPending ? 'Сохранение…' : 'Сохранить'}
          </button>
        </div>
      </header>

      {(msg || err || settingsQuery.isError) && (
        <div className={`tg-banner ${err || settingsQuery.isError ? 'is-bad' : 'is-ok'}`}>
          {err || (settingsQuery.error as Error | undefined)?.message || msg}
        </div>
      )}

      <section className="tg-hero">
        <div className="tg-hero__status">
          <span className={`tg-dot tg-dot--${statusTone}`} />
          <div>
            <strong>{status === 'running' ? 'Бот работает' : status === 'error' ? 'Ошибка бота' : 'Статус бота'}</strong>
            <p>
              {d?.botUsername ? `@${d.botUsername.replace(/^@/, '')}` : 'username ещё не получен'}
              {d?.runtimeDetail ? ` · ${d.runtimeDetail}` : ''}
            </p>
          </div>
        </div>
        <label className="tg-switch">
          <input
            type="checkbox"
            checked={form.enabled}
            onChange={(e) => setForm((f) => ({ ...f, enabled: e.target.checked }))}
          />
          <span>{form.enabled ? 'Включён' : 'Выключен'}</span>
        </label>
        <div className="tg-hero__stats">
          <div>
            <em>{usersQuery.data?.customersLinked ?? '—'}</em>
            <span>клиентов</span>
          </div>
          <div>
            <em>{usersQuery.data?.employeesLinked ?? '—'}</em>
            <span>сотрудников</span>
          </div>
          <div>
            <em>{form.allowedUsers.length}</em>
            <span>в доступе</span>
          </div>
        </div>
      </section>

      <nav className="tg-tabs" aria-label="Разделы Telegram">
        {SECTIONS.map((s) => (
          <button
            key={s.id}
            type="button"
            className={section === s.id ? 'on' : ''}
            onClick={() => setSection(s.id)}
          >
            {s.label}
          </button>
        ))}
      </nav>

      {section === 'setup' && (
        <section className="tg-card">
          <h2>Подключение бота</h2>
          <p className="tg-lead">
            Создайте бота у @BotFather, вставьте токен и включите. После /start бот покажет ваш Telegram ID.
          </p>

          <div className="tg-fields">
            <label className="tg-field">
              <span>Токен{d?.hasToken ? ` · сейчас ${d.botTokenMasked}` : ''}</span>
              <input
                type="password"
                autoComplete="off"
                placeholder={d?.hasToken ? 'Пусто = не менять' : '123456:AA…'}
                value={form.botToken}
                onChange={(e) => setForm((f) => ({ ...f, botToken: e.target.value }))}
              />
            </label>

            <label className="tg-field">
              <span>HTTPS адрес Mini App</span>
              <input
                type="url"
                placeholder="https://tg.shift-club.kz"
                value={form.publicWebAppBaseUrl ?? ''}
                onChange={(e) => setForm((f) => ({ ...f, publicWebAppBaseUrl: e.target.value }))}
              />
              <small>Нужен для сканера QR внутри Telegram. Без слэша в конце.</small>
            </label>

            <label className="tg-field">
              <span>Сотрудник по умолчанию</span>
              <select
                value={form.defaultEmployeeId ?? ''}
                onChange={(e) => setForm((f) => ({ ...f, defaultEmployeeId: e.target.value || null }))}
              >
                <option value="">— не задан —</option>
                {activeEmployees.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.displayName} ({e.login})
                  </option>
                ))}
              </select>
              <small>Если Telegram ID в доступе без привязки к сотруднику.</small>
            </label>
          </div>
        </section>
      )}

      {section === 'access' && (
        <section className="tg-card">
          <h2>Кто управляет ботом</h2>
          <p className="tg-lead">
            Только эти Telegram ID видят staff-меню и кнопки зала. Привязка к сотруднику нужна для аудита.
          </p>

          <div className="tg-access-list">
            {form.allowedUsers.length === 0 && (
              <div className="tg-empty">Список пуст — staff-меню бота недоступно никому.</div>
            )}
            {form.allowedUsers.map((u) => (
              <div key={u.telegramUserId} className="tg-access-row">
                <div className="tg-access-row__id">
                  <strong>{u.telegramUserId}</strong>
                  <span>{u.displayName || 'без имени'}</span>
                </div>
                <select
                  value={u.employeeId ?? ''}
                  onChange={(e) =>
                    setForm((f) => ({
                      ...f,
                      allowedUsers: f.allowedUsers.map((x) =>
                        x.telegramUserId === u.telegramUserId
                          ? { ...x, employeeId: e.target.value || null }
                          : x,
                      ),
                    }))
                  }
                >
                  <option value="">Сотрудник…</option>
                  {activeEmployees.map((e) => (
                    <option key={e.id} value={e.id}>
                      {e.displayName}
                    </option>
                  ))}
                </select>
                <label className="tg-check">
                  <input
                    type="checkbox"
                    checked={u.receiveAlerts}
                    onChange={(e) =>
                      setForm((f) => ({
                        ...f,
                        allowedUsers: f.allowedUsers.map((x) =>
                          x.telegramUserId === u.telegramUserId
                            ? { ...x, receiveAlerts: e.target.checked }
                            : x,
                        ),
                      }))
                    }
                  />
                  Алерты
                </label>
                <button
                  type="button"
                  className="ghost"
                  onClick={() =>
                    setForm((f) => ({
                      ...f,
                      allowedUsers: f.allowedUsers.filter((x) => x.telegramUserId !== u.telegramUserId),
                    }))
                  }
                >
                  Убрать
                </button>
              </div>
            ))}
          </div>

          <div className="tg-add-row">
            <label className="tg-field">
              <span>Telegram ID</span>
              <input value={newUserId} onChange={(e) => setNewUserId(e.target.value)} placeholder="123456789" />
            </label>
            <label className="tg-field">
              <span>Имя</span>
              <input value={newUserName} onChange={(e) => setNewUserName(e.target.value)} placeholder="Админ" />
            </label>
            <label className="tg-field">
              <span>Сотрудник</span>
              <select value={newUserEmployee} onChange={(e) => setNewUserEmployee(e.target.value)}>
                <option value="">—</option>
                {activeEmployees.map((e) => (
                  <option key={e.id} value={e.id}>
                    {e.displayName}
                  </option>
                ))}
              </select>
            </label>
            <button type="button" className="ghost" onClick={addUser}>
              Добавить
            </button>
          </div>
        </section>
      )}

      {section === 'people' && (
        <section className="tg-card">
          <h2>Люди с Telegram</h2>
          <p className="tg-lead">Отвязка TG и назначение клиента сотрудником бота.</p>

          <div className="tg-promote">
            <h3>Сделать сотрудником</h3>
            <div className="tg-add-row">
              <label className="tg-field">
                <span>Клиент с Telegram</span>
                <select value={makeStaffCustomerId} onChange={(e) => setMakeStaffCustomerId(e.target.value)}>
                  <option value="">—</option>
                  {(usersQuery.data?.customers ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.fullName || c.phone} · {c.telegramUserId}
                    </option>
                  ))}
                </select>
              </label>
              <label className="tg-field">
                <span>Карточка сотрудника</span>
                <select value={makeStaffEmployeeId} onChange={(e) => setMakeStaffEmployeeId(e.target.value)}>
                  <option value="">—</option>
                  {activeEmployees.map((e) => (
                    <option key={e.id} value={e.id}>
                      {e.displayName} ({e.login})
                    </option>
                  ))}
                </select>
              </label>
              <button type="button" disabled={makeStaffMutation.isPending} onClick={() => makeStaffMutation.mutate()}>
                Привязать
              </button>
            </div>
          </div>

          <div className="tg-subtabs">
            <button
              type="button"
              className={peopleTab === 'customers' ? 'on' : ''}
              onClick={() => setPeopleTab('customers')}
            >
              Клиенты ({usersQuery.data?.customersLinked ?? 0})
            </button>
            <button
              type="button"
              className={peopleTab === 'staff' ? 'on' : ''}
              onClick={() => setPeopleTab('staff')}
            >
              Сотрудники ({usersQuery.data?.employeesLinked ?? 0})
            </button>
          </div>

          {(usersMsg || usersErr) && (
            <div className={`tg-banner ${usersErr ? 'is-bad' : 'is-ok'}`} style={{ marginTop: 12 }}>
              {usersErr || usersMsg}
            </div>
          )}

          <div className="tg-people-list">
            {peopleTab === 'customers' &&
              ((usersQuery.data?.customers ?? []).length === 0 ? (
                <div className="tg-empty">Нет клиентов с привязанным Telegram</div>
              ) : (
                (usersQuery.data?.customers ?? []).map((c) => (
                  <div key={c.id} className="tg-person">
                    <div>
                      <strong>{c.fullName || '—'}</strong>
                      <span>
                        {c.phone} · TG {c.telegramUserId}
                      </span>
                    </div>
                    <button
                      type="button"
                      className="ghost"
                      disabled={unlinkCustomerMutation.isPending}
                      onClick={() => {
                        if (confirm(`Отвязать Telegram у ${c.fullName || c.phone}?`)) {
                          unlinkCustomerMutation.mutate(c.id)
                        }
                      }}
                    >
                      Отвязать
                    </button>
                  </div>
                ))
              ))}

            {peopleTab === 'staff' &&
              ((usersQuery.data?.employees ?? []).length === 0 ? (
                <div className="tg-empty">Нет сотрудников с Telegram</div>
              ) : (
                (usersQuery.data?.employees ?? []).map((e) => (
                  <div key={e.id} className="tg-person">
                    <div>
                      <strong>{e.displayName}</strong>
                      <span>
                        {e.login}
                        {e.telegramUserId != null ? ` · TG ${e.telegramUserId}` : ' · без TG'}
                        {e.inAllowlist ? ' · в доступе бота' : ''}
                      </span>
                    </div>
                    {e.telegramUserId != null && (
                      <button
                        type="button"
                        className="ghost"
                        disabled={unlinkEmployeeMutation.isPending}
                        onClick={() => {
                          if (confirm(`Отвязать Telegram у ${e.displayName}?`)) {
                            unlinkEmployeeMutation.mutate(e.id)
                          }
                        }}
                      >
                        Отвязать
                      </button>
                    )}
                  </div>
                ))
              ))}
          </div>
        </section>
      )}

      {section === 'alerts' && (
        <section className="tg-card">
          <h2>Уведомления</h2>
          <p className="tg-lead">Что слать сотрудникам в бот и в групповые чаты.</p>

          <div className="tg-checks">
            {(
              [
                ['notifyHelp', 'Вызов администратора'],
                ['notifySecurity', 'Безопасность / обход Shell'],
                ['notifyBar', 'Заказы бара'],
                ['notifySessionWarning', 'Мало времени на сеансе'],
                ['notifyBooking', 'Новые брони'],
              ] as const
            ).map(([key, label]) => (
              <label key={key} className="tg-check-card">
                <input
                  type="checkbox"
                  checked={form[key]}
                  onChange={(e) => setForm((f) => ({ ...f, [key]: e.target.checked }))}
                />
                <span>{label}</span>
              </label>
            ))}
          </div>

          <label className="tg-field" style={{ marginTop: 18 }}>
            <span>Групповые чаты (ID через запятую)</span>
            <input
              value={alertChatsText}
              onChange={(e) => setAlertChatsText(e.target.value)}
              placeholder="-1001234567890"
            />
            <small>Бот должен быть добавлен в группу/канал.</small>
          </label>
        </section>
      )}

      {section === 'crm' && (
        <section className="tg-card">
          <h2>Автосообщения клиентам</h2>
          <p className="tg-lead">
            Мягкие напоминания: давно не был, лежит ключ CASE, идёт акция. Только тем, у кого включены
            уведомления. Тихие часы, лимиты и паузы между сообщениями — чтобы не выключали бота.
          </p>

          {(crmMsg || crmErr || crmQuery.isError) && (
            <div className={`tg-banner ${crmErr || crmQuery.isError ? 'is-bad' : 'is-ok'}`}>
              {crmErr || (crmQuery.error as Error | undefined)?.message || crmMsg}
            </div>
          )}

          <div className="tg-hero__stats" style={{ marginBottom: 18 }}>
            <div>
              <em>{crmQuery.data?.stats.linkedCustomers ?? '—'}</em>
              <span>с TG</span>
            </div>
            <div>
              <em>{crmQuery.data?.stats.sentToday ?? '—'}</em>
              <span>сегодня</span>
            </div>
            <div>
              <em>{crmQuery.data?.stats.winbackCandidates ?? '—'}</em>
              <span>вернуть</span>
            </div>
            <div>
              <em>{crmQuery.data?.stats.unusedKeyCandidates ?? '—'}</em>
              <span>ключ</span>
            </div>
            <div>
              <em>{crmQuery.data?.stats.promoCandidates ?? '—'}</em>
              <span>акция</span>
            </div>
          </div>

          <label className="tg-check-card" style={{ marginBottom: 14 }}>
            <input
              type="checkbox"
              checked={crmForm.enabled}
              onChange={(e) => setCrmForm((f) => ({ ...f, enabled: e.target.checked }))}
            />
            <span>Автосообщения включены</span>
          </label>

          <div className="tg-fields">
            <label className="tg-field">
              <span>С какого часа слать</span>
              <input
                type="number"
                min={0}
                max={23}
                value={crmForm.quietHourFrom}
                onChange={(e) => setCrmForm((f) => ({ ...f, quietHourFrom: Number(e.target.value) || 0 }))}
              />
            </label>
            <label className="tg-field">
              <span>До какого часа</span>
              <input
                type="number"
                min={1}
                max={24}
                value={crmForm.quietHourTo}
                onChange={(e) => setCrmForm((f) => ({ ...f, quietHourTo: Number(e.target.value) || 21 }))}
              />
              <small>По времени клуба. Сейчас по умолчанию 11:00–21:00.</small>
            </label>
            <label className="tg-field">
              <span>Пауза между любыми авто, дни</span>
              <input
                type="number"
                min={2}
                max={30}
                value={crmForm.minDaysBetweenMessages}
                onChange={(e) =>
                  setCrmForm((f) => ({ ...f, minDaysBetweenMessages: Number(e.target.value) || 5 }))
                }
              />
            </label>
            <label className="tg-field">
              <span>Макс. за тик / в день</span>
              <div style={{ display: 'flex', gap: 8 }}>
                <input
                  type="number"
                  min={1}
                  max={100}
                  value={crmForm.maxSendsPerTick}
                  onChange={(e) => setCrmForm((f) => ({ ...f, maxSendsPerTick: Number(e.target.value) || 20 }))}
                />
                <input
                  type="number"
                  min={5}
                  max={500}
                  value={crmForm.maxSendsPerDay}
                  onChange={(e) => setCrmForm((f) => ({ ...f, maxSendsPerDay: Number(e.target.value) || 60 }))}
                />
              </div>
            </label>
          </div>

          <h3 style={{ marginTop: 22 }}>Вернуть в зал</h3>
          <label className="tg-check-card" style={{ marginBottom: 10 }}>
            <input
              type="checkbox"
              checked={crmForm.winbackEnabled}
              onChange={(e) => setCrmForm((f) => ({ ...f, winbackEnabled: e.target.checked }))}
            />
            <span>Писать тем, кто давно не заходил</span>
          </label>
          <div className="tg-fields">
            <label className="tg-field">
              <span>Не был, дней</span>
              <input
                type="number"
                min={5}
                max={60}
                value={crmForm.winbackAfterDays}
                onChange={(e) => setCrmForm((f) => ({ ...f, winbackAfterDays: Number(e.target.value) || 10 }))}
              />
            </label>
            <label className="tg-field">
              <span>Повтор win-back, дни</span>
              <input
                type="number"
                min={7}
                max={90}
                value={crmForm.winbackCooldownDays}
                onChange={(e) =>
                  setCrmForm((f) => ({ ...f, winbackCooldownDays: Number(e.target.value) || 21 }))
                }
              />
            </label>
          </div>

          <h3 style={{ marginTop: 22 }}>Ключ SHIFT CASE</h3>
          <label className="tg-check-card" style={{ marginBottom: 10 }}>
            <input
              type="checkbox"
              checked={crmForm.unusedKeyEnabled}
              onChange={(e) => setCrmForm((f) => ({ ...f, unusedKeyEnabled: e.target.checked }))}
            />
            <span>Напоминать про неоткрытый ключ</span>
          </label>
          <div className="tg-fields">
            <label className="tg-field">
              <span>Не заходил, дней</span>
              <input
                type="number"
                min={1}
                max={30}
                value={crmForm.unusedKeyMinIdleDays}
                onChange={(e) =>
                  setCrmForm((f) => ({ ...f, unusedKeyMinIdleDays: Number(e.target.value) || 3 }))
                }
              />
            </label>
            <label className="tg-field">
              <span>Повтор про ключ, дни</span>
              <input
                type="number"
                min={5}
                max={60}
                value={crmForm.unusedKeyCooldownDays}
                onChange={(e) =>
                  setCrmForm((f) => ({ ...f, unusedKeyCooldownDays: Number(e.target.value) || 14 }))
                }
              />
            </label>
          </div>

          <h3 style={{ marginTop: 22 }}>Акция</h3>
          <label className="tg-check-card" style={{ marginBottom: 10 }}>
            <input
              type="checkbox"
              checked={crmForm.promoNudgeEnabled}
              onChange={(e) => setCrmForm((f) => ({ ...f, promoNudgeEnabled: e.target.checked }))}
            />
            <span>Упоминать активную скидку на пакеты</span>
          </label>
          <div className="tg-fields">
            <label className="tg-field">
              <span>Пауза промо, дни</span>
              <input
                type="number"
                min={5}
                max={45}
                value={crmForm.promoNudgeCooldownDays}
                onChange={(e) =>
                  setCrmForm((f) => ({ ...f, promoNudgeCooldownDays: Number(e.target.value) || 10 }))
                }
              />
              <small>Один раз на кампанию; не слать тем, кто был вчера.</small>
            </label>
          </div>

          <div className="tg-broadcast-actions" style={{ marginTop: 20 }}>
            <button type="button" disabled={crmSaveMutation.isPending} onClick={() => crmSaveMutation.mutate()}>
              {crmSaveMutation.isPending ? 'Сохранение…' : 'Сохранить авто'}
            </button>
          </div>
        </section>
      )}

      {section === 'broadcast' && (
        <section className="tg-card">
          <h2>Рассылка</h2>
          <p className="tg-lead">Сообщение уйдёт в бот тем, у кого привязан Telegram.</p>

          <div className="tg-fields">
            <label className="tg-field">
              <span>Кому</span>
              <select
                value={broadcastAudience}
                onChange={(e) => setBroadcastAudience(e.target.value as 'customers' | 'staff' | 'both')}
              >
                <option value="customers">Клиенты</option>
                <option value="staff">Сотрудники</option>
                <option value="both">Клиенты и сотрудники</option>
              </select>
            </label>
            <label className="tg-field">
              <span>Текст</span>
              <textarea
                rows={6}
                value={broadcastText}
                onChange={(e) => setBroadcastText(e.target.value)}
                placeholder="Например: завтра с 18:00 турнир — регистрация на кассе"
              />
            </label>
          </div>

          <div className="tg-broadcast-actions">
            <button
              type="button"
              disabled={broadcastMutation.isPending || broadcastText.trim().length < 2}
              onClick={() => broadcastMutation.mutate()}
            >
              {broadcastMutation.isPending ? 'Отправка…' : 'Отправить'}
            </button>
            {broadcastMsg && <span className="flash">{broadcastMsg}</span>}
            {broadcastErr && <span className="error">{broadcastErr}</span>}
          </div>
        </section>
      )}

      {section !== 'crm' && (
        <div className="tg-footer-save">
          <button type="button" disabled={saveMutation.isPending} onClick={() => saveMutation.mutate()}>
            {saveMutation.isPending ? 'Сохранение…' : 'Сохранить настройки'}
          </button>
          <span className="muted">Изменения доступа и токена применяются после сохранения.</span>
        </div>
      )}
    </div>
  )
}
