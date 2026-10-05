import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { apiFetch, downloadAuthenticated } from '../api/client'
import { ActionDialog } from '../components/ActionDialog'
import {
  cashShiftStatusLabel,
  formatDuration,
  money,
  paymentMethodLabel,
  receiptItemTypeLabel,
} from '../format'

type Tab = 'overview' | 'sales' | 'shifts' | 'load' | 'bar' | 'customers'

type OverviewDto = {
  revenueTotal: number
  collectedTotal: number
  depositsTotal: number
  walletRedeemedTotal: number
  receiptCount: number
  sessionCount: number
  bookingCount: number
  barRevenue: number
  gamesRevenue: number
  packagesRevenue: number
  caseKeysRevenue?: number
  caseKeysSold?: number
  caseOpenings?: number
  casePrizeCostEstimate?: number
  casePrizeBalanceCredits?: number
  casePrizeTimeMinutes?: number
  newCustomers: number
  openShiftsCollected: number
  liabilityBalances: number
  liabilityBonuses: number
  prevRevenueTotal: number
  revenueDeltaPercent?: number | null
}

type MoneyRow = { key: string; amount: number; count: number }

type SalesDto = {
  receiptCount: number
  grossTotal: number
  discountTotal: number
  revenueTotal: number
  collectedTotal: number
  depositsTotal: number
  walletRedeemedTotal: number
  averageCheck: number
  byPaymentMethod: MoneyRow[]
  byItemType: MoneyRow[]
  byDay: MoneyRow[]
}

type ShiftsDto = {
  shiftCount: number
  totalSalesCash: number
  totalSalesCard: number
  totalSalesKaspi: number
  totalSalesTransfer: number
  totalSalesOther: number
  totalDiscrepancyAbs: number
  shifts: {
    id: string
    number: string
    cashRegisterName: string
    status: string | number
    openedAt: string
    closedAt?: string
    salesCash: number
    salesCard: number
    salesKaspi: number
    salesTransfer: number
    salesOther: number
    depositsTotal: number
    discrepancy?: number
    receiptsTotal: number
    receiptCount: number
  }[]
}

type LoadDto = {
  sessionCount: number
  totalMinutes: number
  totalRevenue: number
  averageDurationMinutes: number
  averageCheck: number
  byComputer: {
    computerName: string
    zoneName?: string
    sessionCount: number
    minutesPlayed: number
    revenue: number
    utilizationPercent: number
  }[]
  peakHours: MoneyRow[]
}

type BarDto = {
  revenue: number
  cost: number
  profit: number
  marginPercent: number
  itemsSold: number
  averageBarCheck: number
  walletBarRevenue: number
  topProducts: {
    name: string
    categoryName: string
    qtySold: number
    revenue: number
    cost: number
    profit: number
  }[]
  byCategory: MoneyRow[]
  lowStock: MoneyRow[]
}

type CustomersDto = {
  totalCustomers: number
  newCustomers: number
  activeCustomers: number
  averageBalance: number
  totalBalances: number
  totalBonuses: number
  totalTimeBankMinutes: number
  topSpenders: MoneyRow[]
}

/** Локальная дата филиала Asia/Almaty (UTC+5), не браузер. */
function todayAlmaty() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Almaty',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date())
  const y = parts.find((p) => p.type === 'year')?.value
  const m = parts.find((p) => p.type === 'month')?.value
  const d = parts.find((p) => p.type === 'day')?.value
  return `${y}-${m}-${d}`
}

function daysAgoAlmaty(days: number) {
  const now = Date.now()
  const shifted = new Date(now - days * 86400000)
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Almaty',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(shifted)
  const y = parts.find((p) => p.type === 'year')?.value
  const m = parts.find((p) => p.type === 'month')?.value
  const d = parts.find((p) => p.type === 'day')?.value
  return `${y}-${m}-${d}`
}

function monthStartAlmaty() {
  const t = todayAlmaty()
  return `${t.slice(0, 8)}01`
}

function yesterdayAlmaty() {
  return daysAgoAlmaty(1)
}

export function ReportsPage() {
  const [tab, setTab] = useState<Tab>('overview')
  const [from, setFrom] = useState(daysAgoAlmaty(6))
  const [to, setTo] = useState(todayAlmaty())
  const qs = useMemo(() => `from=${from}&to=${to}`, [from, to])

  const overviewQuery = useQuery({
    queryKey: ['report-overview', qs],
    queryFn: async () => (await apiFetch<OverviewDto>(`/api/reports/overview?${qs}`)).data!,
    enabled: tab === 'overview',
  })
  const salesQuery = useQuery({
    queryKey: ['report-sales', qs],
    queryFn: async () => (await apiFetch<SalesDto>(`/api/reports/sales?${qs}`)).data!,
    enabled: tab === 'sales',
  })
  const shiftsQuery = useQuery({
    queryKey: ['report-shifts', qs],
    queryFn: async () => (await apiFetch<ShiftsDto>(`/api/reports/shifts?${qs}`)).data!,
    enabled: tab === 'shifts',
  })
  const loadQuery = useQuery({
    queryKey: ['report-load', qs],
    queryFn: async () => (await apiFetch<LoadDto>(`/api/reports/load?${qs}`)).data!,
    enabled: tab === 'load',
  })
  const barQuery = useQuery({
    queryKey: ['report-bar', qs],
    queryFn: async () => (await apiFetch<BarDto>(`/api/reports/bar?${qs}`)).data!,
    enabled: tab === 'bar',
  })
  const customersQuery = useQuery({
    queryKey: ['report-customers', qs],
    queryFn: async () => (await apiFetch<CustomersDto>(`/api/reports/customers?${qs}`)).data!,
    enabled: tab === 'customers',
  })

  const activeQuery =
    tab === 'overview'
      ? overviewQuery
      : tab === 'sales'
        ? salesQuery
        : tab === 'shifts'
          ? shiftsQuery
          : tab === 'load'
            ? loadQuery
            : tab === 'bar'
              ? barQuery
              : customersQuery

  const [zOpen, setZOpen] = useState(false)
  const [zReport, setZReport] = useState<{
    number: string
    cashRegisterName: string
    openedAt: string
    closedAt?: string
    collectedTotal: number
    revenueTotal: number
    depositsTotal: number
    expectedCash: number
    salesCash: number
    salesCard: number
    salesKaspi: number
    salesTransfer: number
    paidReceiptCount: number
    refundedReceiptCount: number
    discrepancy?: number
    byItemType: { key: string; amount: number }[]
  } | null>(null)

  async function exportCsv(kind: Tab) {
    await downloadAuthenticated(`/api/reports/${kind}/export?${qs}`, `${kind}_${from}_${to}.csv`)
  }

  async function openZ(shiftId: string) {
    const z = (await apiFetch<NonNullable<typeof zReport>>(`/api/cash/shifts/${shiftId}/z-report`)).data
    if (!z) return
    setZReport(z)
    setZOpen(true)
  }

  const tabs: { id: Tab; label: string }[] = [
    { id: 'overview', label: 'Сводка' },
    { id: 'sales', label: 'Продажи' },
    { id: 'shifts', label: 'Смены' },
    { id: 'load', label: 'Загрузка' },
    { id: 'bar', label: 'Бар' },
    { id: 'customers', label: 'Клиенты' },
  ]

  const presets: { label: string; apply: () => void }[] = [
    {
      label: 'Сегодня',
      apply: () => {
        setFrom(todayAlmaty())
        setTo(todayAlmaty())
      },
    },
    {
      label: 'Вчера',
      apply: () => {
        const y = yesterdayAlmaty()
        setFrom(y)
        setTo(y)
      },
    },
    {
      label: '7 дней',
      apply: () => {
        setFrom(daysAgoAlmaty(6))
        setTo(todayAlmaty())
      },
    },
    {
      label: '30 дней',
      apply: () => {
        setFrom(daysAgoAlmaty(29))
        setTo(todayAlmaty())
      },
    },
    {
      label: 'Месяц',
      apply: () => {
        setFrom(monthStartAlmaty())
        setTo(todayAlmaty())
      },
    },
  ]

  return (
    <div className="reports-page">
      <header className="page-head reports-head">
        <div>
          <h1>Отчёты</h1>
          <p className="muted">
            Сутки клуба — Asia/Almaty (UTC+5). Выручка = игры + бар + пакеты (без пополнений баланса).
          </p>
        </div>
        <div className="reports-head-actions no-print">
          <button type="button" className="ghost" onClick={() => void exportCsv(tab)}>
            CSV
          </button>
          <button type="button" className="ghost" onClick={() => window.print()}>
            Печать
          </button>
        </div>
      </header>

      <div className="reports-toolbar no-print">
        <div className="reports-period">
          <label>
            С
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label>
            По
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
        </div>
        <div className="reports-presets">
          {presets.map((p) => (
            <button key={p.label} type="button" className="ghost" onClick={p.apply}>
              {p.label}
            </button>
          ))}
        </div>
      </div>

      <div className="reports-tabs no-print" role="tablist">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            className={tab === t.id ? 'reports-tab is-active' : 'reports-tab'}
            onClick={() => setTab(t.id)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {activeQuery.isLoading && (
        <div className="reports-state">
          <div className="reports-skeleton" />
          <div className="reports-skeleton" />
          <div className="reports-skeleton" />
        </div>
      )}

      {activeQuery.isError && (
        <div className="reports-state reports-state--error">
          <p>Не удалось загрузить отчёт.</p>
          <button type="button" onClick={() => void activeQuery.refetch()}>
            Повторить
          </button>
        </div>
      )}

      {!activeQuery.isLoading && !activeQuery.isError && tab === 'overview' && overviewQuery.data && (
        <section className="reports-panel">
          <div className="reports-kpi">
            <Kpi
              title="Выручка клуба"
              value={`${money(overviewQuery.data.revenueTotal)} ₸`}
              hint="Игры + бар + пакеты + ключи CASE + списания с баланса"
              accent
              delta={overviewQuery.data.revenueDeltaPercent}
            />
            <Kpi
              title="Собрано в кассу"
              value={`${money(overviewQuery.data.collectedTotal)} ₸`}
              hint={`${overviewQuery.data.receiptCount} чеков · включая депозиты`}
            />
            <Kpi title="Депозиты" value={`${money(overviewQuery.data.depositsTotal)} ₸`} hint="Обязательства, не прибыль" />
            <Kpi title="С балансов" value={`${money(overviewQuery.data.walletRedeemedTotal)} ₸`} />
          </div>

          <h2 className="reports-h2">Структура выручки</h2>
          <div className="reports-kpi reports-kpi--3">
            <Kpi title="Игры" value={`${money(overviewQuery.data.gamesRevenue)} ₸`} />
            <Kpi title="Бар" value={`${money(overviewQuery.data.barRevenue)} ₸`} />
            <Kpi title="Пакеты" value={`${money(overviewQuery.data.packagesRevenue)} ₸`} />
            <Kpi
              title="Ключи CASE"
              value={`${money(overviewQuery.data.caseKeysRevenue ?? 0)} ₸`}
              hint={`${overviewQuery.data.caseKeysSold ?? 0} шт.`}
            />
          </div>
          <SplitBar
            parts={[
              { label: 'Игры', amount: overviewQuery.data.gamesRevenue, color: 'var(--accent, #3b82f6)' },
              { label: 'Бар', amount: overviewQuery.data.barRevenue, color: '#f59e0b' },
              { label: 'Пакеты', amount: overviewQuery.data.packagesRevenue, color: '#10b981' },
              {
                label: 'Ключи CASE',
                amount: overviewQuery.data.caseKeysRevenue ?? 0,
                color: '#a855f7',
              },
            ]}
          />

          <h2 className="reports-h2">SHIFT CASE</h2>
          <div className="reports-kpi">
            <Kpi title="Открытий кейса" value={String(overviewQuery.data.caseOpenings ?? 0)} />
            <Kpi
              title="Оценка призов"
              value={`${money(overviewQuery.data.casePrizeCostEstimate ?? 0)} ₸`}
              hint="CostEstimate выпавших призов"
            />
            <Kpi
              title="Призы на баланс"
              value={`${money(overviewQuery.data.casePrizeBalanceCredits ?? 0)} ₸`}
              hint="Начисления гостям · не выручка"
            />
            <Kpi
              title="Призы временем"
              value={`${overviewQuery.data.casePrizeTimeMinutes ?? 0} мин`}
            />
          </div>

          <div className="reports-kpi" style={{ marginTop: 16 }}>
            <Kpi title="Сеансы" value={String(overviewQuery.data.sessionCount)} />
            <Kpi title="Брони" value={String(overviewQuery.data.bookingCount)} />
            <Kpi title="Новые клиенты" value={String(overviewQuery.data.newCustomers)} />
            <Kpi title="Открытые смены" value={`${money(overviewQuery.data.openShiftsCollected)} ₸`} hint="Собрано сейчас" />
            <Kpi title="Балансы клиентов" value={`${money(overviewQuery.data.liabilityBalances)} ₸`} hint="Текущие обязательства" />
            <Kpi title="Бонусы" value={`${money(overviewQuery.data.liabilityBonuses)} ₸`} />
          </div>
          {overviewQuery.data.prevRevenueTotal > 0 && (
            <p className="muted reports-note">
              Предыдущий период той же длины: {money(overviewQuery.data.prevRevenueTotal)} ₸
              {overviewQuery.data.revenueDeltaPercent != null &&
                ` · Δ ${overviewQuery.data.revenueDeltaPercent > 0 ? '+' : ''}${overviewQuery.data.revenueDeltaPercent}%`}
            </p>
          )}
        </section>
      )}

      {!activeQuery.isLoading && !activeQuery.isError && tab === 'sales' && salesQuery.data && (
        <section className="reports-panel">
          <div className="reports-kpi">
            <Kpi
              title="Выручка"
              value={`${money(salesQuery.data.revenueTotal)} ₸`}
              hint={`чек ср. ${money(salesQuery.data.averageCheck)} ₸`}
              accent
            />
            <Kpi title="Собрано" value={`${money(salesQuery.data.collectedTotal)} ₸`} />
            <Kpi title="Депозиты" value={`${money(salesQuery.data.depositsTotal)} ₸`} />
            <Kpi title="С баланса" value={`${money(salesQuery.data.walletRedeemedTotal)} ₸`} />
            <Kpi title="Скидки" value={`${money(salesQuery.data.discountTotal)} ₸`} />
            <Kpi title="Чеков" value={String(salesQuery.data.receiptCount)} />
          </div>
          <h2 className="reports-h2">По дням</h2>
          <DayBars rows={salesQuery.data.byDay} />
          <ReportTable
            columns={['День', 'Сумма', 'Чеков']}
            rows={salesQuery.data.byDay.map((r) => [r.key, `${money(r.amount)} ₸`, String(r.count)])}
          />
          <h2 className="reports-h2">Способы оплаты</h2>
          <ReportTable
            columns={['Способ', 'Сумма', 'Операций']}
            rows={salesQuery.data.byPaymentMethod.map((r) => [
              paymentMethodLabel(r.key),
              `${money(r.amount)} ₸`,
              String(r.count),
            ])}
          />
          <h2 className="reports-h2">Типы позиций</h2>
          <ReportTable
            columns={['Тип', 'Сумма', 'Кол-во']}
            rows={salesQuery.data.byItemType.map((r) => [
              receiptItemTypeLabel(r.key),
              `${money(r.amount)} ₸`,
              String(r.count),
            ])}
          />
        </section>
      )}

      {!activeQuery.isLoading && !activeQuery.isError && tab === 'shifts' && shiftsQuery.data && (
        <section className="reports-panel">
          <div className="reports-kpi">
            <Kpi title="Смен" value={String(shiftsQuery.data.shiftCount)} accent />
            <Kpi title="Нал" value={`${money(shiftsQuery.data.totalSalesCash)} ₸`} />
            <Kpi title="Карта" value={`${money(shiftsQuery.data.totalSalesCard)} ₸`} />
            <Kpi title="Kaspi" value={`${money(shiftsQuery.data.totalSalesKaspi)} ₸`} />
            <Kpi title="Перевод" value={`${money(shiftsQuery.data.totalSalesTransfer)} ₸`} />
            <Kpi title="|Δ|" value={`${money(shiftsQuery.data.totalDiscrepancyAbs)} ₸`} hint="Модуль расхождений" />
          </div>
          {shiftsQuery.data.shifts.length === 0 ? (
            <p className="muted">Нет смен за период</p>
          ) : (
            <div className="reports-shift-list">
              {shiftsQuery.data.shifts.map((s) => (
                <article key={s.id} className="reports-shift-card">
                  <header>
                    <strong>{s.number}</strong>
                    <span className="chip">{cashShiftStatusLabel(s.status)}</span>
                  </header>
                  <p className="muted">
                    {s.cashRegisterName} · {new Date(s.openedAt).toLocaleString('ru-RU')}
                    {s.closedAt ? ` → ${new Date(s.closedAt).toLocaleString('ru-RU')}` : ' (открыта)'}
                  </p>
                  <p>
                    Нал {money(s.salesCash)} · карта {money(s.salesCard)} · Kaspi {money(s.salesKaspi)} · перевод{' '}
                    {money(s.salesTransfer)} · депозиты {money(s.depositsTotal)} · чеков {s.receiptCount} (
                    {money(s.receiptsTotal)} ₸)
                    {s.discrepancy != null && ` · Δ ${money(s.discrepancy)}`}
                  </p>
                  <button type="button" className="ghost no-print" onClick={() => void openZ(s.id)}>
                    Z/X-отчёт
                  </button>
                </article>
              ))}
            </div>
          )}
        </section>
      )}

      {!activeQuery.isLoading && !activeQuery.isError && tab === 'load' && loadQuery.data && (
        <section className="reports-panel">
          <div className="reports-kpi">
            <Kpi title="Сеансов" value={String(loadQuery.data.sessionCount)} accent />
            <Kpi title="Игровое время" value={formatDuration(loadQuery.data.totalMinutes)} />
            <Kpi title="Выручка сеансов" value={`${money(loadQuery.data.totalRevenue)} ₸`} />
            <Kpi title="Ср. длит." value={formatDuration(loadQuery.data.averageDurationMinutes)} />
          </div>
          <h2 className="reports-h2">Пиковые часы</h2>
          <DayBars rows={loadQuery.data.peakHours} labelKey />
          <ReportTable
            columns={['Час', 'Выручка', 'Сеансов']}
            rows={loadQuery.data.peakHours.map((r) => [r.key, `${money(r.amount)} ₸`, String(r.count)])}
          />
          <h2 className="reports-h2">По ПК</h2>
          <ReportTable
            columns={['ПК', 'Зона', 'Сеансы', 'Время', 'Выручка', 'Загрузка']}
            rows={loadQuery.data.byComputer.map((c) => [
              c.computerName,
              c.zoneName ?? '—',
              String(c.sessionCount),
              formatDuration(c.minutesPlayed),
              `${money(c.revenue)} ₸`,
              `${c.utilizationPercent}%`,
            ])}
          />
        </section>
      )}

      {!activeQuery.isLoading && !activeQuery.isError && tab === 'bar' && barQuery.data && (
        <section className="reports-panel">
          <div className="reports-kpi">
            <Kpi
              title="Выручка"
              value={`${money(barQuery.data.revenue)} ₸`}
              hint={`из них с баланса ${money(barQuery.data.walletBarRevenue)}`}
              accent
            />
            <Kpi title="Себестоимость" value={`${money(barQuery.data.cost)} ₸`} />
            <Kpi title="Прибыль" value={`${money(barQuery.data.profit)} ₸`} hint={`${barQuery.data.marginPercent}% маржа`} />
            <Kpi title="Продано шт." value={String(barQuery.data.itemsSold)} />
          </div>
          <h2 className="reports-h2">Топ товаров</h2>
          <ReportTable
            columns={['Товар', 'Категория', 'Шт', 'Выручка', 'Прибыль']}
            rows={barQuery.data.topProducts.map((p) => [
              p.name,
              p.categoryName,
              String(p.qtySold),
              `${money(p.revenue)} ₸`,
              `${money(p.profit)} ₸`,
            ])}
          />
          <h2 className="reports-h2">Категории</h2>
          <ReportTable
            columns={['Категория', 'Сумма', 'Позиций']}
            rows={barQuery.data.byCategory.map((r) => [r.key, `${money(r.amount)} ₸`, String(r.count)])}
          />
          <h2 className="reports-h2">Низкий остаток</h2>
          <ReportTable
            columns={['Товар', 'Остаток', 'Мин.']}
            rows={barQuery.data.lowStock.map((r) => [r.key, String(r.amount), String(r.count)])}
          />
        </section>
      )}

      {!activeQuery.isLoading && !activeQuery.isError && tab === 'customers' && customersQuery.data && (
        <section className="reports-panel">
          <div className="reports-kpi">
            <Kpi title="Всего" value={String(customersQuery.data.totalCustomers)} accent />
            <Kpi title="Новые" value={String(customersQuery.data.newCustomers)} />
            <Kpi
              title="Активные"
              value={String(customersQuery.data.activeCustomers)}
              hint="Сеанс или списание с баланса"
            />
            <Kpi title="Ср. баланс" value={`${money(customersQuery.data.averageBalance)} ₸`} />
            <Kpi title="Сумма балансов" value={`${money(customersQuery.data.totalBalances)} ₸`} hint="Обязательства" />
            <Kpi title="Бонусы" value={`${money(customersQuery.data.totalBonuses)} ₸`} />
            <Kpi title="Банк времени" value={formatDuration(customersQuery.data.totalTimeBankMinutes)} />
          </div>
          <h2 className="reports-h2">Топ по тратам за период</h2>
          <ReportTable
            columns={['Клиент', 'Списано', 'Визиты']}
            rows={customersQuery.data.topSpenders.map((r) => [
              r.key,
              `${money(r.amount)} ₸`,
              String(r.count),
            ])}
          />
        </section>
      )}

      <ActionDialog
        open={zOpen && !!zReport}
        onClose={() => setZOpen(false)}
        title={zReport?.closedAt ? `Z-отчёт · ${zReport.number}` : `X-отчёт · ${zReport?.number ?? ''}`}
        description={zReport ? `${zReport.cashRegisterName} · ${new Date(zReport.openedAt).toLocaleString('ru-RU')}` : undefined}
        size="md"
        footer={
          <button type="button" onClick={() => setZOpen(false)}>
            Закрыть
          </button>
        }
      >
        {zReport && (
          <div className="form-stack">
            <div className="cash-stats">
              <div>
                <span className="muted">Собрано</span>
                <strong>{money(zReport.collectedTotal)} ₸</strong>
              </div>
              <div>
                <span className="muted">Выручка</span>
                <strong>{money(zReport.revenueTotal)} ₸</strong>
              </div>
              <div>
                <span className="muted">Депозиты</span>
                <strong>{money(zReport.depositsTotal)} ₸</strong>
              </div>
              <div>
                <span className="muted">В ящике</span>
                <strong>{money(zReport.expectedCash)} ₸</strong>
              </div>
            </div>
            <p className="muted">
              Нал {money(zReport.salesCash)} · карта {money(zReport.salesCard)} · Kaspi {money(zReport.salesKaspi)} ·
              перевод {money(zReport.salesTransfer)}
            </p>
            <p className="muted">
              Чеков {zReport.paidReceiptCount} · возвратов {zReport.refundedReceiptCount}
              {zReport.discrepancy != null ? ` · Δ ${money(zReport.discrepancy)}` : ''}
            </p>
            <ReportTable
              columns={['Тип', 'Сумма']}
              rows={zReport.byItemType.map((r) => [receiptItemTypeLabel(r.key), `${money(r.amount)} ₸`])}
            />
          </div>
        )}
      </ActionDialog>
    </div>
  )
}

function Kpi({
  title,
  value,
  hint,
  accent,
  delta,
}: {
  title: string
  value: string
  hint?: string
  accent?: boolean
  delta?: number | null
}) {
  return (
    <article className={accent ? 'reports-kpi-card is-accent' : 'reports-kpi-card'}>
      <span className="reports-kpi-label">{title}</span>
      <strong className="reports-kpi-value">{value}</strong>
      {hint && <span className="muted reports-kpi-hint">{hint}</span>}
      {delta != null && (
        <span className={delta >= 0 ? 'reports-delta is-up' : 'reports-delta is-down'}>
          {delta > 0 ? '+' : ''}
          {delta}% к пред. периоду
        </span>
      )}
    </article>
  )
}

function ReportTable({ columns, rows }: { columns: string[]; rows: string[][] }) {
  if (!rows.length) return <p className="muted">Нет данных</p>
  return (
    <div className="reports-table-wrap">
      <table className="reports-table">
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c}>{c}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {row.map((cell, j) => (
                <td key={j}>{cell}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function DayBars({ rows, labelKey }: { rows: MoneyRow[]; labelKey?: boolean }) {
  if (!rows.length) return null
  const max = Math.max(...rows.map((r) => r.amount), 1)
  return (
    <div className="reports-bars no-print">
      {rows.map((r) => (
        <div key={r.key} className="reports-bar-row">
          <span className="reports-bar-label">{labelKey ? r.key : r.key.slice(5)}</span>
          <div className="reports-bar-track">
            <i style={{ width: `${Math.max(4, (r.amount / max) * 100)}%` }} />
          </div>
          <span className="reports-bar-val">{money(r.amount)}</span>
        </div>
      ))}
    </div>
  )
}

function SplitBar({ parts }: { parts: { label: string; amount: number; color: string }[] }) {
  const total = parts.reduce((s, p) => s + p.amount, 0)
  if (total <= 0) return null
  return (
    <div className="reports-split" title={parts.map((p) => `${p.label}: ${money(p.amount)}`).join(' · ')}>
      {parts
        .filter((p) => p.amount > 0)
        .map((p) => (
          <i key={p.label} style={{ width: `${(p.amount / total) * 100}%`, background: p.color }} />
        ))}
    </div>
  )
}
