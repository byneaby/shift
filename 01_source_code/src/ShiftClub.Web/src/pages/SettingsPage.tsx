import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { apiFetch } from '../api/client'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import { money } from '../format'

type SettingsTab = 'loyalty' | 'rewards' | 'promo' | 'system'

type LoyaltyLevel = {
  id: string
  branchId: string
  name: string
  code: string
  minSpent: number
  bonusPercent: number
  timeDiscountPercent: number
  sortOrder: number
  isActive: boolean
}

type LoyaltyForm = {
  name: string
  code: string
  minSpent: string
  bonusPercent: string
  timeDiscountPercent: string
  sortOrder: string
  isActive: boolean
}

const emptyLoyalty: LoyaltyForm = {
  name: '',
  code: '',
  minSpent: '0',
  bonusPercent: '0',
  timeDiscountPercent: '0',
  sortOrder: '0',
  isActive: true,
}

function toLoyaltyForm(l: LoyaltyLevel): LoyaltyForm {
  return {
    name: l.name,
    code: l.code,
    minSpent: String(l.minSpent),
    bonusPercent: String(l.bonusPercent),
    timeDiscountPercent: String(l.timeDiscountPercent),
    sortOrder: String(l.sortOrder),
    isActive: l.isActive,
  }
}

function nextSortOrder(levels: LoyaltyLevel[]) {
  if (levels.length === 0) return '1'
  return String(Math.max(...levels.map((l) => l.sortOrder)) + 1)
}

export default function SettingsPage() {
  const [tab, setTab] = useState<SettingsTab>('loyalty')

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Настройки</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Лояльность, награды и служебные операции клуба
          </p>
        </div>
      </header>

      <div className="settings-page">
        <nav className="settings-tabs" role="tablist" aria-label="Разделы настроек">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'loyalty'}
            className={tab === 'loyalty' ? 'is-active' : undefined}
            onClick={() => setTab('loyalty')}
          >
            Лояльность
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'rewards'}
            className={tab === 'rewards' ? 'is-active' : undefined}
            onClick={() => setTab('rewards')}
          >
            Награды
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'promo'}
            className={tab === 'promo' ? 'is-active' : undefined}
            onClick={() => setTab('promo')}
          >
            Акция −%
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'system'}
            className={tab === 'system' ? 'is-active' : undefined}
            onClick={() => setTab('system')}
          >
            Система
          </button>
        </nav>

        {tab === 'loyalty' ? (
          <LoyaltySection />
        ) : tab === 'rewards' ? (
          <RewardsSection />
        ) : tab === 'promo' ? (
          <MarketingPromoSection />
        ) : (
          <SystemSection />
        )}
      </div>
    </>
  )
}

function LoyaltySection() {
  const qc = useQueryClient()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<LoyaltyForm>(emptyLoyalty)
  const [error, setError] = useState<string | null>(null)
  const [deleteId, setDeleteId] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)

  const levelsQ = useQuery({
    queryKey: ['loyalty-levels'],
    queryFn: async () =>
      (await apiFetch<LoyaltyLevel[]>('/api/loyalty-levels')).data ?? [],
  })

  const levels = useMemo(() => {
    const rows = [...(levelsQ.data ?? [])]
    rows.sort((a, b) => a.minSpent - b.minSpent || a.sortOrder - b.sortOrder)
    return rows
  }, [levelsQ.data])

  const deleteTarget = levels.find((l) => l.id === deleteId) ?? null

  const saveMut = useMutation({
    mutationFn: async () => {
      const body = {
        name: form.name.trim(),
        code: form.code.trim(),
        minSpent: Number(form.minSpent) || 0,
        bonusPercent: Number(form.bonusPercent) || 0,
        timeDiscountPercent: Number(form.timeDiscountPercent) || 0,
        sortOrder: Number(form.sortOrder) || 0,
        isActive: form.isActive,
      }
      if (editingId) {
        return apiFetch(`/api/loyalty-levels/${editingId}`, {
          method: 'PUT',
          body: JSON.stringify(body),
        })
      }
      return apiFetch('/api/loyalty-levels', {
        method: 'POST',
        body: JSON.stringify(body),
      })
    },
    onSuccess: async () => {
      setError(null)
      setFlash(editingId ? 'Уровень обновлён' : 'Уровень добавлен')
      setDialogOpen(false)
      setEditingId(null)
      setForm(emptyLoyalty)
      await qc.invalidateQueries({ queryKey: ['loyalty-levels'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка сохранения'),
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) =>
      apiFetch(`/api/loyalty-levels/${id}`, { method: 'DELETE' }),
    onSuccess: async () => {
      setDeleteId(null)
      setFlash('Уровень удалён')
      await qc.invalidateQueries({ queryKey: ['loyalty-levels'] })
    },
    onError: (e) => {
      setDeleteId(null)
      setFlash(e instanceof Error ? e.message : 'Не удалось удалить')
    },
  })

  function openCreate() {
    setEditingId(null)
    setForm({
      ...emptyLoyalty,
      sortOrder: nextSortOrder(levelsQ.data ?? []),
      minSpent: levels.length === 0 ? '0' : String((levels[levels.length - 1]?.minSpent ?? 0) + 10000),
    })
    setError(null)
    setDialogOpen(true)
  }

  function openEdit(l: LoyaltyLevel) {
    setEditingId(l.id)
    setForm(toLoyaltyForm(l))
    setError(null)
    setDialogOpen(true)
  }

  function closeDialog() {
    if (saveMut.isPending) return
    setDialogOpen(false)
    setEditingId(null)
    setForm(emptyLoyalty)
    setError(null)
  }

  return (
    <div className="settings-section">
      {flash && (
        <p className="flash" role="status">
          {flash}
        </p>
      )}

      <section className="panel settings-howto">
        <h2 className="section-title">Как это работает</h2>
        <div className="settings-howto-grid">
          <article>
            <span className="settings-howto-step">1</span>
            <div>
              <strong>Порог трат</strong>
              <p className="muted">Клиент автоматически получает уровень, когда сумма покупок достигает порога.</p>
            </div>
          </article>
          <article>
            <span className="settings-howto-step">2</span>
            <div>
              <strong>Бонус при пополнении</strong>
              <p className="muted">При депозите на кассе на баланс падает % бонусов по текущему уровню.</p>
            </div>
          </article>
          <article>
            <span className="settings-howto-step">3</span>
            <div>
              <strong>Скидка на время</strong>
              <p className="muted">При старте и продлении сеанса цена игрового времени уменьшается на этот %.</p>
            </div>
          </article>
        </div>
        <p className="muted settings-howto-note">
          Уровень можно зафиксировать вручную в карточке клиента — тогда авто-повышение не сработает.
        </p>
      </section>

      <div className="page-toolbar">
        <div className="page-toolbar-left">
          <h2 className="section-title" style={{ margin: 0 }}>
            Уровни
          </h2>
          <span className="chip">{levels.length}</span>
        </div>
        <div className="page-toolbar-right">
          <button type="button" onClick={openCreate}>
            + Новый уровень
          </button>
        </div>
      </div>

      {levelsQ.isError && (
        <p className="error">{(levelsQ.error as Error).message}</p>
      )}

      {levelsQ.isLoading ? (
        <p className="muted">Загрузка…</p>
      ) : levels.length === 0 ? (
        <section className="panel settings-empty">
          <strong>Уровней пока нет</strong>
          <p className="muted">
            Создайте лестницу, например: Бронза → Серебро → Золото. Пороги и проценты можно менять в любой момент.
          </p>
          <button type="button" onClick={openCreate}>
            Создать первый уровень
          </button>
        </section>
      ) : (
        <div className="settings-loyalty-ladder">
          {levels.map((l, index) => {
            const prev = index > 0 ? levels[index - 1] : null
            return (
              <article
                key={l.id}
                className={`settings-level-card${l.isActive ? '' : ' is-inactive'}`}
              >
                <div className="settings-level-rank">{index + 1}</div>
                <div className="settings-level-main">
                  <div className="settings-level-title">
                    <strong>{l.name}</strong>
                    <code className="settings-level-code">{l.code}</code>
                    {!l.isActive && <span className="chip chip-warn">выкл</span>}
                  </div>
                  <p className="muted settings-level-threshold">
                    от {money(l.minSpent)} ₸
                    {prev
                      ? ` · после «${prev.name}»`
                      : ' · стартовый уровень'}
                  </p>
                  <div className="settings-level-perks">
                    <div className="settings-perk">
                      <span>Бонус пополнения</span>
                      <strong>{formatPct(l.bonusPercent)}</strong>
                    </div>
                    <div className="settings-perk">
                      <span>Скидка на время</span>
                      <strong>{formatPct(l.timeDiscountPercent)}</strong>
                    </div>
                  </div>
                </div>
                <div className="settings-level-actions">
                  <button type="button" className="ghost" onClick={() => openEdit(l)}>
                    Изменить
                  </button>
                  <button type="button" className="ghost" onClick={() => setDeleteId(l.id)}>
                    Удалить
                  </button>
                </div>
              </article>
            )
          })}
        </div>
      )}

      <ActionDialog
        open={dialogOpen}
        onClose={closeDialog}
        title={editingId ? 'Изменить уровень' : 'Новый уровень'}
        description="Проценты применяются сразу после сохранения. Порог — сумма всех трат клиента."
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={closeDialog} disabled={saveMut.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={saveMut.isPending || !form.name.trim() || !form.code.trim()}
              onClick={() => saveMut.mutate()}
            >
              {saveMut.isPending ? 'Сохранение…' : editingId ? 'Сохранить' : 'Добавить'}
            </button>
          </>
        }
      >
        {error && <p className="error">{error}</p>}
        <div className="form-stack">
          <div className="field-grid">
            <label>
              Название
              <input
                value={form.name}
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                placeholder="Серебро"
                autoFocus
              />
            </label>
            <label>
              Код
              <input
                value={form.code}
                onChange={(e) => setForm((f) => ({ ...f, code: e.target.value.toUpperCase() }))}
                placeholder="SILVER"
              />
            </label>
          </div>

          <label>
            Порог трат, ₸
            <input
              type="number"
              min={0}
              step={1000}
              value={form.minSpent}
              onChange={(e) => setForm((f) => ({ ...f, minSpent: e.target.value }))}
            />
            <span className="field-hint">Клиент получит уровень, когда TotalSpent ≥ этого значения.</span>
          </label>

          <div className="field-grid">
            <label>
              Бонус при пополнении, %
              <input
                type="number"
                min={0}
                max={100}
                step={0.5}
                value={form.bonusPercent}
                onChange={(e) => setForm((f) => ({ ...f, bonusPercent: e.target.value }))}
              />
              <span className="field-hint">Начисляется бонусами на кассе.</span>
            </label>
            <label>
              Скидка на игровое время, %
              <input
                type="number"
                min={0}
                max={100}
                step={0.5}
                value={form.timeDiscountPercent}
                onChange={(e) => setForm((f) => ({ ...f, timeDiscountPercent: e.target.value }))}
              />
              <span className="field-hint">Старт и продление сеанса.</span>
            </label>
          </div>

          <div className="field-grid">
            <label>
              Порядок в списке
              <input
                type="number"
                value={form.sortOrder}
                onChange={(e) => setForm((f) => ({ ...f, sortOrder: e.target.value }))}
              />
            </label>
            <label className="check-row settings-check-align">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))}
              />
              Участвует в авто-повышении
            </label>
          </div>
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!deleteId}
        onClose={() => setDeleteId(null)}
        title="Удалить уровень?"
        message={
          deleteTarget
            ? `«${deleteTarget.name}» будет удалён. Если он назначен клиентам — удаление не пройдёт: сначала смените им уровень или отключите этот.`
            : 'Уровень будет удалён.'
        }
        confirmLabel="Удалить"
        danger
        loading={deleteMut.isPending}
        onConfirm={() => {
          if (deleteId) deleteMut.mutate(deleteId)
        }}
      />
    </div>
  )
}

function RewardsSection() {
  const qc = useQueryClient()
  const me = JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as { roles?: string[] } | null
  const isOwner = (me?.roles ?? []).some((r) => String(r).toLowerCase() === 'owner')
  const [flash, setFlash] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [enabled, setEnabled] = useState(true)
  const [birthdayBonus, setBirthdayBonus] = useState('500')
  const [birthdayMinutes, setBirthdayMinutes] = useState('60')
  const [cooldownDays, setCooldownDays] = useState('30')
  const [webAppUrl, setWebAppUrl] = useState('')
  const [depositBonusEnabled, setDepositBonusEnabled] = useState(true)
  const [depositStackLoyalty, setDepositStackLoyalty] = useState(true)
  const [depositTiers, setDepositTiers] = useState<{ minAmount: string; bonusAmount: string }[]>([
    { minAmount: '3000', bonusAmount: '300' },
    { minAmount: '5000', bonusAmount: '500' },
    { minAmount: '10000', bonusAmount: '1500' },
  ])
  const [tiers, setTiers] = useState<
    { days: string; bonusAmount: string; barRewards: string }[]
  >([
    { days: '3', bonusAmount: '200', barRewards: '0' },
    { days: '5', bonusAmount: '500', barRewards: '1' },
    { days: '7', bonusAmount: '1000', barRewards: '1' },
  ])

  const q = useQuery({
    queryKey: ['engagement-settings'],
    queryFn: async () =>
      (
        await apiFetch<{
          enabled: boolean
          streakTiers: { days: number; bonusAmount: number; barRewards: number }[]
          birthdayBonusAmount: number
          birthdayTimeBankMinutes: number
          telegramChangeCooldownDays: number
          publicWebAppBaseUrl: string | null
          depositBonusEnabled: boolean
          depositBonusStackWithLoyaltyPercent: boolean
          depositBonusTiers: { minAmount: number; bonusAmount: number }[]
        }>('/api/settings/engagement')
      ).data!,
  })

  useEffect(() => {
    const d = q.data
    if (!d) return
    setEnabled(d.enabled)
    setBirthdayBonus(String(d.birthdayBonusAmount))
    setBirthdayMinutes(String(d.birthdayTimeBankMinutes))
    setCooldownDays(String(d.telegramChangeCooldownDays))
    setWebAppUrl(d.publicWebAppBaseUrl ?? '')
    setDepositBonusEnabled(d.depositBonusEnabled !== false)
    setDepositStackLoyalty(d.depositBonusStackWithLoyaltyPercent !== false)
    setDepositTiers(
      (d.depositBonusTiers?.length
        ? d.depositBonusTiers
        : [
            { minAmount: 3000, bonusAmount: 300 },
            { minAmount: 5000, bonusAmount: 500 },
            { minAmount: 10000, bonusAmount: 1500 },
          ]
      ).map((t) => ({
        minAmount: String(t.minAmount),
        bonusAmount: String(t.bonusAmount),
      })),
    )
    setTiers(
      (d.streakTiers?.length ? d.streakTiers : [
        { days: 3, bonusAmount: 200, barRewards: 0 },
        { days: 5, bonusAmount: 500, barRewards: 1 },
        { days: 7, bonusAmount: 1000, barRewards: 1 },
      ]).map((t) => ({
        days: String(t.days),
        bonusAmount: String(t.bonusAmount),
        barRewards: String(t.barRewards),
      })),
    )
  }, [q.data])

  const saveMut = useMutation({
    mutationFn: async () => {
      const body = {
        enabled,
        streakTiers: tiers.map((t) => ({
          days: Number(t.days) || 0,
          bonusAmount: Number(t.bonusAmount) || 0,
          barRewards: Number(t.barRewards) || 0,
        })),
        birthdayBonusAmount: Number(birthdayBonus) || 0,
        birthdayTimeBankMinutes: Number(birthdayMinutes) || 0,
        telegramChangeCooldownDays: Number(cooldownDays) || 30,
        publicWebAppBaseUrl: webAppUrl.trim() || '',
        depositBonusEnabled,
        depositBonusStackWithLoyaltyPercent: depositStackLoyalty,
        depositBonusTiers: depositTiers.map((t) => ({
          minAmount: Number(t.minAmount) || 0,
          bonusAmount: Number(t.bonusAmount) || 0,
        })),
      }
      const res = await apiFetch('/api/settings/engagement', {
        method: 'PUT',
        body: JSON.stringify(body),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сохранить')
    },
    onSuccess: async () => {
      setError(null)
      setFlash('Параметры наград сохранены')
      await qc.invalidateQueries({ queryKey: ['engagement-settings'] })
      setTimeout(() => setFlash(null), 2500)
    },
    onError: (e) => {
      setFlash(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  return (
    <div className="settings-section">
      <section className="panel">
        <div className="settings-danger-head" style={{ marginBottom: 16 }}>
          <div>
            <h2 className="section-title">Награды и вовлечённость</h2>
            <p className="muted" style={{ margin: 0 }}>
              Бонус за пополнение, серия посещений, день рождения и лимит смены Telegram.
            </p>
          </div>
        </div>

        {flash && (
          <p className="flash" role="status">
            {flash}
          </p>
        )}
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}

        <label className="check-row" style={{ marginBottom: 16 }}>
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          Награды включены
        </label>

        <h3 style={{ margin: '0 0 8px', fontSize: 15 }}>Бонус за пополнение</h3>
        <p className="muted" style={{ margin: '0 0 12px', fontSize: 13 }}>
          При пополнении баланса от указанной суммы на бонусный счёт начисляется фиксированная сумма.
          Срабатывает лучший подходящий порог (например, 7000 ₸ → порог «от 5000»).
        </p>
        <label className="check-row" style={{ marginBottom: 10 }}>
          <input
            type="checkbox"
            checked={depositBonusEnabled}
            onChange={(e) => setDepositBonusEnabled(e.target.checked)}
          />
          Бонус за сумму пополнения включён
        </label>
        <label className="check-row" style={{ marginBottom: 14 }}>
          <input
            type="checkbox"
            checked={depositStackLoyalty}
            onChange={(e) => setDepositStackLoyalty(e.target.checked)}
            disabled={!depositBonusEnabled}
          />
          Суммировать с % бонусом уровня лояльности
        </label>
        <div className="settings-loyalty-ladder" style={{ marginBottom: 22, opacity: depositBonusEnabled ? 1 : 0.55 }}>
          {depositTiers.map((t, idx) => (
            <div key={idx} className="list-card" style={{ padding: 14, marginBottom: 10 }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr auto', gap: 10 }}>
                <label>
                  <span className="muted" style={{ fontSize: 12 }}>Пополнение от, ₸</span>
                  <input
                    value={t.minAmount}
                    disabled={!depositBonusEnabled}
                    onChange={(e) =>
                      setDepositTiers((rows) =>
                        rows.map((r, i) => (i === idx ? { ...r, minAmount: e.target.value } : r)),
                      )
                    }
                  />
                </label>
                <label>
                  <span className="muted" style={{ fontSize: 12 }}>Бонус, ₸</span>
                  <input
                    value={t.bonusAmount}
                    disabled={!depositBonusEnabled}
                    onChange={(e) =>
                      setDepositTiers((rows) =>
                        rows.map((r, i) => (i === idx ? { ...r, bonusAmount: e.target.value } : r)),
                      )
                    }
                  />
                </label>
                <button
                  type="button"
                  className="ghost"
                  style={{ alignSelf: 'end' }}
                  disabled={!depositBonusEnabled || depositTiers.length <= 1}
                  onClick={() => setDepositTiers((rows) => rows.filter((_, i) => i !== idx))}
                >
                  Удалить
                </button>
              </div>
            </div>
          ))}
          <button
            type="button"
            className="ghost"
            disabled={!depositBonusEnabled}
            onClick={() => setDepositTiers((rows) => [...rows, { minAmount: '', bonusAmount: '0' }])}
          >
            Добавить порог
          </button>
        </div>

        <h3 style={{ margin: '0 0 10px', fontSize: 15 }}>Серия посещений</h3>
        <div className="settings-loyalty-ladder" style={{ marginBottom: 18 }}>
          {tiers.map((t, idx) => (
            <div key={idx} className="list-card" style={{ padding: 14, marginBottom: 10 }}>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr auto', gap: 10 }}>
                <label>
                  <span className="muted" style={{ fontSize: 12 }}>Дней</span>
                  <input
                    value={t.days}
                    onChange={(e) =>
                      setTiers((rows) => rows.map((r, i) => (i === idx ? { ...r, days: e.target.value } : r)))
                    }
                  />
                </label>
                <label>
                  <span className="muted" style={{ fontSize: 12 }}>Бонусы, ₸</span>
                  <input
                    value={t.bonusAmount}
                    onChange={(e) =>
                      setTiers((rows) =>
                        rows.map((r, i) => (i === idx ? { ...r, bonusAmount: e.target.value } : r)),
                      )
                    }
                  />
                </label>
                <label>
                  <span className="muted" style={{ fontSize: 12 }}>Напитков</span>
                  <input
                    value={t.barRewards}
                    onChange={(e) =>
                      setTiers((rows) =>
                        rows.map((r, i) => (i === idx ? { ...r, barRewards: e.target.value } : r)),
                      )
                    }
                  />
                </label>
                <button
                  type="button"
                  className="ghost"
                  style={{ alignSelf: 'end' }}
                  onClick={() => setTiers((rows) => rows.filter((_, i) => i !== idx))}
                  disabled={tiers.length <= 1}
                >
                  Удалить
                </button>
              </div>
            </div>
          ))}
          <button
            type="button"
            className="ghost"
            onClick={() => setTiers((rows) => [...rows, { days: '', bonusAmount: '0', barRewards: '0' }])}
          >
            Добавить порог
          </button>
        </div>

        <h3 style={{ margin: '8px 0 10px', fontSize: 15 }}>День рождения</h3>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, maxWidth: 480, marginBottom: 18 }}>
          <label>
            <span className="muted" style={{ fontSize: 12 }}>Бонусы, ₸</span>
            <input value={birthdayBonus} onChange={(e) => setBirthdayBonus(e.target.value)} />
          </label>
          <label>
            <span className="muted" style={{ fontSize: 12 }}>Минут в банк</span>
            <input value={birthdayMinutes} onChange={(e) => setBirthdayMinutes(e.target.value)} />
          </label>
        </div>

        <h3 style={{ margin: '8px 0 10px', fontSize: 15 }}>Telegram</h3>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, maxWidth: 320, marginBottom: 14 }}>
          <span className="muted" style={{ fontSize: 12 }}>Интервал смены Telegram, дней</span>
          <input value={cooldownDays} onChange={(e) => setCooldownDays(e.target.value)} />
        </label>
        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, maxWidth: 560, marginBottom: 18 }}>
          <span className="muted" style={{ fontSize: 12 }}>
            HTTPS-адрес Mini App (сканер QR в боте), например https://club.example.com
          </span>
          <input
            value={webAppUrl}
            placeholder="https://…"
            onChange={(e) => setWebAppUrl(e.target.value)}
          />
          <span className="muted" style={{ fontSize: 12 }}>
            Без публичного HTTPS кнопка «Сканировать QR» в боте недоступна (ограничение Telegram). Камера телефона и
            ввод кода работают всегда.
          </span>
        </label>

        {isOwner ? (
          <button type="button" className="primary" disabled={saveMut.isPending} onClick={() => saveMut.mutate()}>
            {saveMut.isPending ? 'Сохранение…' : 'Сохранить награды'}
          </button>
        ) : (
          <p className="muted">Изменение параметров доступно владельцу (owner).</p>
        )}
      </section>
    </div>
  )
}

function SystemSection() {
  return (
    <div className="settings-section">
      <KaspiPosSection />
      <SystemDangerSection />
    </div>
  )
}

function KaspiPosSection() {
  const qc = useQueryClient()
  const me = JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as { roles?: string[] } | null
  const isOwner = (me?.roles ?? []).some((r) => String(r).toLowerCase() === 'owner')
  const [host, setHost] = useState('')
  const [flash, setFlash] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const statusQ = useQuery({
    queryKey: ['kaspi-pos-status'],
    queryFn: async () => (await apiFetch<{
      configured: boolean
      registered: boolean
      ready: boolean
      host: string | null
      terminalId: string
      registerName: string | null
      message: string | null
      tokenExpiresAt: string | null
    }>('/api/kaspi-pos/status')).data!,
    refetchInterval: 30000,
  })

  const configQ = useQuery({
    queryKey: ['kaspi-pos-config'],
    enabled: isOwner,
    queryFn: async () =>
      (await apiFetch<{ host: string | null; terminalId: string; registerName: string }>('/api/kaspi-pos/config'))
        .data!,
  })

  useEffect(() => {
    if (configQ.data?.host !== undefined) setHost(configQ.data.host ?? '')
  }, [configQ.data?.host])

  const saveHostMut = useMutation({
    mutationFn: async () =>
      apiFetch('/api/kaspi-pos/config', {
        method: 'PUT',
        body: JSON.stringify({ host: host.trim() || null }),
      }),
    onSuccess: async () => {
      setFlash('IP терминала сохранён')
      setError(null)
      await qc.invalidateQueries({ queryKey: ['kaspi-pos-status'] })
      await qc.invalidateQueries({ queryKey: ['kaspi-pos-config'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  const registerMut = useMutation({
    mutationFn: async () => apiFetch('/api/kaspi-pos/register', { method: 'POST', body: '{}' }),
    onSuccess: async () => {
      setFlash('Терминал подключён. Kaspi QR на панели будет отправлять сумму на POS.')
      setError(null)
      await qc.invalidateQueries({ queryKey: ['kaspi-pos-status'] })
    },
    onError: (e) =>
      setError(
        e instanceof Error
          ? e.message
          : 'Ошибка регистрации. На терминале: Админ → Защита интеграции → Разрешить.',
      ),
  })

  const st = statusQ.data

  return (
    <section className="panel" style={{ marginBottom: 16 }}>
      <h2 className="section-title">Kaspi Smart POS</h2>
      <p className="muted" style={{ marginTop: 0, fontSize: 13 }}>
        При оплате «Kaspi QR» сумма уходит на терминал автоматически (LAN, порт 8080). ID терминала:{' '}
        <strong>{st?.terminalId ?? '32555483'}</strong>
      </p>

      {flash && (
        <p className="flash" onAnimationEnd={() => setFlash(null)}>
          {flash}
        </p>
      )}
      {error && <p className="error">{error}</p>}

      <div className="ops-stats" style={{ marginBottom: 12 }}>
        <div
          className="ops-stat"
          style={{ ['--stat-accent' as string]: st?.ready ? '#3ddc97' : st?.configured ? '#fbbf24' : '#64748b' }}
        >
          <span>Статус</span>
          <strong>{st?.ready ? 'Готов' : st?.configured ? 'Нужна регистрация' : 'Не настроен'}</strong>
        </div>
      </div>

      {st?.message && !st.ready && <p className="muted">{st.message}</p>}

      {isOwner ? (
        <div className="form-stack" style={{ maxWidth: 480 }}>
          <label>
            IP терминала (LAN)
            <input
              value={host}
              onChange={(e) => setHost(e.target.value)}
              placeholder="192.168.1.50"
            />
          </label>
          <div className="order-actions">
            <button type="button" disabled={saveHostMut.isPending} onClick={() => saveHostMut.mutate()}>
              {saveHostMut.isPending ? 'Сохранение…' : 'Сохранить IP'}
            </button>
            <button
              type="button"
              className="primary"
              disabled={!host.trim() || registerMut.isPending}
              onClick={() => registerMut.mutate()}
            >
              {registerMut.isPending ? 'Подключение…' : 'Подключить терминал'}
            </button>
          </div>
          <p className="muted" style={{ fontSize: 12, margin: 0 }}>
            Перед «Подключить»: на терминале включите «Защита интеграции» → «Настроить доступ», затем нажмите
            кнопку и выберите «Разрешить» на экране POS.
          </p>
        </div>
      ) : (
        <p className="muted">Настройка IP и регистрация — только для owner.</p>
      )}
    </section>
  )
}

function SystemDangerSection() {
  const scopes = [
    { id: 'cash', label: 'Касса', desc: 'чеки, платежи, кассовые смены, нумерация документов' },
    { id: 'bar', label: 'Бар / продажи товаров', desc: 'заказы бара, складские движения, остатки → 0' },
    { id: 'sessions', label: 'Сеансы ПК', desc: 'игровые сессии и история' },
    { id: 'balances', label: 'Балансы клиентов', desc: 'обнулить кошельки + удалить транзакции/пакеты' },
    { id: 'payroll', label: 'Зарплата', desc: 'начисления и рабочие смены сотрудников' },
  ] as const

  const [selected, setSelected] = useState<Record<string, boolean>>({
    cash: true,
    bar: true,
    sessions: true,
    balances: true,
    payroll: true,
  })
  const [confirming, setConfirming] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  const [resultOk, setResultOk] = useState(true)

  const picked = scopes.filter((s) => selected[s.id]).map((s) => s.id)
  const allOn = picked.length === scopes.length

  const resetMut = useMutation({
    mutationFn: () =>
      apiFetch<{ message: string; scopes?: string[] }>('/api/system/reset-data', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          confirm: true,
          scopes: allOn ? ['all'] : picked,
        }),
      }),
    onSuccess: (r) => {
      setResult(r.data?.message ?? 'Данные сброшены')
      setResultOk(true)
      setConfirming(false)
    },
    onError: (e) => {
      setResult(e instanceof Error ? e.message : 'Ошибка')
      setResultOk(false)
      setConfirming(false)
    },
  })

  return (
    <div className="settings-section">
      <section className="panel settings-danger">
        <div className="settings-danger-head">
          <div>
            <h2 className="section-title">Опасная зона</h2>
            <p className="muted" style={{ margin: 0 }}>
              Операции без отката. Каталог товаров, тарифы, ПК и карточки клиентов не удаляются.
            </p>
          </div>
          <span className="chip chip-warn">необратимо</span>
        </div>

        <div className="settings-danger-card">
          <div>
            <strong>Выборочный сброс данных</strong>
            <div className="settings-reset-scopes">
              <label className="settings-reset-scope">
                <input
                  type="checkbox"
                  checked={allOn}
                  onChange={(e) => {
                    const on = e.target.checked
                    setSelected(Object.fromEntries(scopes.map((s) => [s.id, on])))
                  }}
                />
                <span>
                  <b>Выбрать всё</b>
                </span>
              </label>
              {scopes.map((s) => (
                <label key={s.id} className="settings-reset-scope">
                  <input
                    type="checkbox"
                    checked={!!selected[s.id]}
                    onChange={(e) => setSelected((prev) => ({ ...prev, [s.id]: e.target.checked }))}
                  />
                  <span>
                    <b>{s.label}</b>
                    <em className="muted">{s.desc}</em>
                  </span>
                </label>
              ))}
            </div>
          </div>

          {result && (
            <p className={resultOk ? 'flash' : 'error'} role="status">
              {result}
            </p>
          )}

          {!confirming ? (
            <button
              type="button"
              className="danger"
              disabled={picked.length === 0}
              onClick={() => setConfirming(true)}
            >
              Сбросить выбранное…
            </button>
          ) : (
            <div className="settings-danger-confirm">
              <p className="error" style={{ margin: 0 }}>
                Точно сбросить: {picked.join(', ')}? Это нельзя откатить.
              </p>
              <div className="action-chip-row">
                <button
                  type="button"
                  className="danger"
                  disabled={resetMut.isPending || picked.length === 0}
                  onClick={() => resetMut.mutate()}
                >
                  {resetMut.isPending ? 'Удаление…' : 'Да, сбросить'}
                </button>
                <button
                  type="button"
                  className="ghost"
                  disabled={resetMut.isPending}
                  onClick={() => setConfirming(false)}
                >
                  Отмена
                </button>
              </div>
            </div>
          )}
        </div>
      </section>
    </div>
  )
}

function MarketingPromoSection() {
  const qc = useQueryClient()
  const me = JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as { roles?: string[] } | null
  const isOwner = (me?.roles ?? []).some((r) => String(r).toLowerCase() === 'owner')
  const [flash, setFlash] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [enabled, setEnabled] = useState(true)
  const [percent, setPercent] = useState('50')
  const [label, setLabel] = useState('1 месяц −50%')
  const [title, setTitle] = useState('Акция')
  const [startsAt, setStartsAt] = useState('')
  const [endsAt, setEndsAt] = useState('')
  const [applyToTariffs, setApplyToTariffs] = useState(true)
  const [activeNow, setActiveNow] = useState(false)

  const q = useQuery({
    queryKey: ['marketing-promo'],
    queryFn: async () =>
      (
        await apiFetch<{
          enabled: boolean
          percent: number
          label: string
          title: string | null
          startsAt: string | null
          endsAt: string | null
          applyToTariffs: boolean
          isActiveNow: boolean
        }>('/api/settings/marketing-promo')
      ).data!,
  })

  useEffect(() => {
    const d = q.data
    if (!d) return
    setEnabled(!!d.enabled)
    setPercent(String(d.percent ?? 50))
    setLabel(d.label || '1 месяц −50%')
    setTitle(d.title || 'Акция')
    setStartsAt(d.startsAt ? d.startsAt.slice(0, 16) : '')
    setEndsAt(d.endsAt ? d.endsAt.slice(0, 16) : '')
    setApplyToTariffs(d.applyToTariffs !== false)
    setActiveNow(!!d.isActiveNow)
  }, [q.data])

  const saveMut = useMutation({
    mutationFn: async () => {
      setError(null)
      setFlash(null)
      const res = await apiFetch('/api/settings/marketing-promo', {
        method: 'PUT',
        body: JSON.stringify({
          enabled,
          percent: Number(percent) || 0,
          label,
          title,
          startsAt: startsAt ? new Date(startsAt).toISOString() : null,
          endsAt: endsAt ? new Date(endsAt).toISOString() : null,
          applyToTariffs,
        }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сохранить')
      return res.data as { isActiveNow?: boolean }
    },
    onSuccess: (d) => {
      setFlash(d?.isActiveNow ? 'Акция сохранена и сейчас активна.' : 'Акция сохранена.')
      setActiveNow(!!d?.isActiveNow)
      void qc.invalidateQueries({ queryKey: ['marketing-promo'] })
    },
    onError: (e: Error) => setError(e.message),
  })

  return (
    <div className="settings-section">
      <section className="panel">
        <div className="settings-danger-head" style={{ marginBottom: 16 }}>
          <div>
            <h2 className="section-title">Маркетинговая скидка</h2>
            <p className="muted" style={{ margin: 0 }}>
              Скидка только на пакеты (2+1, 3+2, день, ночь) — прайс, Shell и касса. Почасовка без акции.
              Каталожные цены в БД не меняются.
            </p>
          </div>
          <span className={`chip ${activeNow ? 'chip--ok' : ''}`}>{activeNow ? 'Сейчас активна' : 'Не активна'}</span>
        </div>

        {flash && (
          <p className="flash" role="status">
            {flash}
          </p>
        )}
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}

        <label className="check-row" style={{ marginBottom: 14 }}>
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          Акция включена
        </label>
        <label className="check-row" style={{ marginBottom: 18 }}>
          <input
            type="checkbox"
            checked={applyToTariffs}
            onChange={(e) => setApplyToTariffs(e.target.checked)}
          />
          Применять к пакетам (2+1, 3+2, день, ночь)
        </label>

        <div className="field-grid" style={{ marginBottom: 16 }}>
          <label>
            Скидка, %
            <input value={percent} onChange={(e) => setPercent(e.target.value)} />
          </label>
          <label>
            Заголовок
            <input value={title} onChange={(e) => setTitle(e.target.value)} />
          </label>
          <label style={{ gridColumn: '1 / -1' }}>
            Бейдж (напр. «1 месяц −50%»)
            <input value={label} onChange={(e) => setLabel(e.target.value)} />
          </label>
          <label>
            Начало
            <input type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
          </label>
          <label>
            Конец
            <input type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
          </label>
        </div>

        {!isOwner ? (
          <p className="muted">Менять акцию может только владелец.</p>
        ) : (
          <button type="button" disabled={saveMut.isPending} onClick={() => saveMut.mutate()}>
            {saveMut.isPending ? 'Сохранение…' : 'Сохранить акцию'}
          </button>
        )}
      </section>
    </div>
  )
}

function formatPct(value: number) {
  const n = Number(value) || 0
  return `${n % 1 === 0 ? n.toFixed(0) : n.toFixed(1)}%`
}
