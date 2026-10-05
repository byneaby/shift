import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { apiFetch, getToken } from '../api/client'
import type { BranchDto } from '../api/client'
import { can } from '../permissions'

const MODULES = [
  { to: '/floor', title: 'Зал', hint: 'Карта ПК и сеансы' },
  { to: '/display', title: 'Экран зала', hint: 'Второй монитор' },
  { to: '/cash', title: 'Касса', hint: 'Смена и чеки', perm: 'cash.view' },
  { to: '/bar', title: 'Бар', hint: 'Продажа и заказы', perm: 'bar.view' },
  { to: '/bookings', title: 'Брони', hint: 'Бронирование', perm: 'bookings.view' },
  { to: '/customers', title: 'Клиенты', hint: 'Баланс и банк', perm: 'customers.view' },
  { to: '/reports', title: 'Отчёты', hint: 'Сводка смены', perm: 'reports.view' },
  { to: '/computers', title: 'Компьютеры', hint: 'ПК и коды', perm: 'computers.manage' },
  { to: '/zones', title: 'Зоны', hint: 'Залы клуба', perm: 'zones.manage' },
  { to: '/tariffs', title: 'Тарифы', hint: 'Цены и пакеты', perm: 'tariffs.manage' },
  { to: '/software', title: 'Программы', hint: 'Каталог игр на ПК', perm: 'computers.view' },
  { to: '/updates', title: 'Обновления', hint: 'Клиент Shell', perm: 'settings.manage' },
  { to: '/employees', title: 'Сотрудники', hint: 'Роли и смены', perm: 'employees.manage' },
] as const

export function DashboardPage() {
  const branchesQuery = useQuery({
    queryKey: ['branches'],
    queryFn: async () => {
      const result = await apiFetch<BranchDto[]>('/api/branches')
      return result.data ?? []
    },
    enabled: !!getToken(),
  })

  const branch = branchesQuery.data?.[0]
  const modules = useMemo(
    () => MODULES.filter((m) => !('perm' in m) || !m.perm || can(m.perm)),
    [],
  )

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Главная</h1>
        </div>
        {branch && (
          <div className="page-head-meta">
            <span className="chip">{branch.code}</span>
            <strong>{branch.name}</strong>
          </div>
        )}
      </header>

      {branchesQuery.isError && <p className="error">{(branchesQuery.error as Error).message}</p>}

      <section className="home-grid">
        {modules.map((m) => (
          <Link key={m.to} to={m.to} className="home-card">
            <h2>{m.title}</h2>
            <p className="muted">{m.hint}</p>
          </Link>
        ))}
      </section>

      {branch && branch.zones.length > 0 && (
        <section className="home-zones">
          <h2>Зоны</h2>
          <ul className="zones">
            {branch.zones.map((zone) => (
              <li key={zone.id}>
                <span className="dot" style={{ background: zone.colorHex }} />
                {zone.name}
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}
