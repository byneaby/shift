import { useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { setToken } from '../api/client'
import type { EmployeeDto } from '../api/client'
import { ChangePasswordDialog } from '../components/ChangePasswordDialog'
import { LicenseBanner } from '../components/LicenseBanner'
import { StaffNotifications } from '../components/StaffNotifications'
import { rolesLabel } from '../format'
import { can } from '../permissions'
import { LicenseFeature, useLicenseStatus } from '../licensing'
import { useDisableBrowserAutofill } from '../disableBrowserAutofill'

const ALL_FEATURES: readonly string[] = Object.values(LicenseFeature)

const NAV = [
  {
    title: 'Смена',
    items: [
      { to: '/floor', label: 'Зал', icon: 'map', hint: 'Карта и сеансы' },
      { to: '/bar', label: 'Бар', icon: 'bar', hint: 'Продажа и заказы', perm: 'bar.view' },
      { to: '/bookings', label: 'Брони', icon: 'book', hint: 'Бронирование', perm: 'bookings.view' },
      { to: '/cash', label: 'Касса', icon: 'cash', hint: 'Смена и чеки', perm: 'cash.view' },
      { to: '/customers', label: 'Клиенты', icon: 'user', hint: 'Баланс и банк', perm: 'customers.view' },
      {
        to: '/cases',
        label: 'Кейсы',
        icon: 'case',
        hint: 'Кейсы и выдача',
        perm: 'customers.view',
        feature: LicenseFeature.ShiftCase,
      },
      { to: '/wiki', label: 'Инструкция', icon: 'wiki', hint: 'Как работать на кассе' },
      { to: '/software', label: 'Программы', icon: 'apps', hint: 'Каталог игр на ПК', perm: 'computers.view' },
      { to: '/computers', label: 'Компьютеры', icon: 'pc', hint: 'ПК и коды', perm: 'computers.manage' },
      { to: '/monitoring', label: 'Мониторинг', icon: 'monitor', hint: 'CPU/GPU по запросу', perm: 'computers.manage' },
    ],
  },
  {
    title: 'Управление',
    items: [
      { to: '/', label: 'Главная', icon: 'home', end: true, hint: 'Обзор' },
      { to: '/reports', label: 'Отчёты', icon: 'report', hint: 'Сводка', perm: 'reports.view' },
      { to: '/zones', label: 'Зоны', icon: 'zone', hint: 'Залы', perm: 'zones.manage' },
      { to: '/tariffs', label: 'Тарифы', icon: 'tariff', hint: 'Цены', perm: 'tariffs.manage' },
      { to: '/employees', label: 'Сотрудники', icon: 'staff', hint: 'Роли и смены', perm: 'employees.manage' },
      { to: '/telegram', label: 'Telegram', icon: 'tg', hint: 'Бот и алерты', perm: 'settings.manage' },
      { to: '/news', label: 'Новости', icon: 'news', hint: 'Лента на Shell', perm: 'settings.manage' },
      { to: '/updates', label: 'Обновления', icon: 'update', hint: 'Клиент Shell', perm: 'settings.manage' },
      { to: '/settings', label: 'Настройки', icon: 'settings', hint: 'Лояльность и система', perm: 'settings.manage' },
    ],
  },
  {
    title: 'Система',
    items: [
      { to: '/license', label: 'Лицензия', icon: 'key', hint: 'Срок и лимит ПК', perm: 'settings.manage' },
      { to: '/backups', label: 'Копии базы', icon: 'save', hint: 'Бэкапы и восстановление', perm: 'settings.manage' },
    ],
  },
] as const

function NavIcon({ name }: { name: string }) {
  const common = {
    width: 18,
    height: 18,
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.8,
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    'aria-hidden': true,
  }
  switch (name) {
    case 'map':
      return (
        <svg {...common}>
          <polygon points="3 6 9 3 15 6 21 3 21 18 15 21 9 18 3 21" />
          <line x1="9" y1="3" x2="9" y2="18" />
          <line x1="15" y1="6" x2="15" y2="21" />
        </svg>
      )
    case 'pc':
      return (
        <svg {...common}>
          <rect x="2" y="3" width="20" height="14" rx="2" />
          <path d="M8 21h8M12 17v4" />
        </svg>
      )
    case 'monitor':
      return (
        <svg {...common}>
          <path d="M4 14h4l2-6 3 10 2-6h5" />
          <circle cx="18" cy="6" r="2" />
        </svg>
      )
    case 'cash':
      return (
        <svg {...common}>
          <rect x="2" y="6" width="20" height="12" rx="2" />
          <circle cx="12" cy="12" r="3" />
        </svg>
      )
    case 'bar':
      return (
        <svg {...common}>
          <path d="M4 4h16l-2 7H6L4 4z" />
          <path d="M8 11v9M16 11v9M6 20h12" />
        </svg>
      )
    case 'book':
      return (
        <svg {...common}>
          <rect x="3" y="4" width="18" height="18" rx="2" />
          <line x1="16" y1="2" x2="16" y2="6" />
          <line x1="8" y1="2" x2="8" y2="6" />
          <line x1="3" y1="10" x2="21" y2="10" />
        </svg>
      )
    case 'user':
      return (
        <svg {...common}>
          <circle cx="12" cy="8" r="4" />
          <path d="M4 21v-1a6 6 0 0 1 12 0v1" />
        </svg>
      )
    case 'case':
      return (
        <svg {...common}>
          <rect x="3" y="8" width="18" height="12" rx="2" />
          <path d="M8 8V6a4 4 0 0 1 8 0v2" />
          <path d="M12 12v4" />
        </svg>
      )
    case 'wiki':
      return (
        <svg {...common}>
          <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20" />
          <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z" />
          <line x1="8" y1="7" x2="16" y2="7" />
          <line x1="8" y1="11" x2="16" y2="11" />
          <line x1="8" y1="15" x2="13" y2="15" />
        </svg>
      )
    case 'home':
      return (
        <svg {...common}>
          <path d="M3 11.5 12 4l9 7.5" />
          <path d="M5 10.5V20h14v-9.5" />
        </svg>
      )
    case 'news':
      return (
        <svg {...common}>
          <path d="M4 4h12v16H4z" />
          <path d="M16 8h4v12a2 2 0 0 1-2 2h-2" />
          <line x1="7" y1="8" x2="13" y2="8" />
          <line x1="7" y1="12" x2="13" y2="12" />
          <line x1="7" y1="16" x2="11" y2="16" />
        </svg>
      )
    case 'zone':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="8" />
          <circle cx="12" cy="12" r="3" />
        </svg>
      )
    case 'apps':
      return (
        <svg {...common}>
          <rect x="3" y="3" width="7" height="7" rx="1.5" />
          <rect x="14" y="3" width="7" height="7" rx="1.5" />
          <rect x="3" y="14" width="7" height="7" rx="1.5" />
          <rect x="14" y="14" width="7" height="7" rx="1.5" />
        </svg>
      )
    case 'tariff':
      return (
        <svg {...common}>
          <line x1="12" y1="1" x2="12" y2="23" />
          <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7H14a3.5 3.5 0 0 1 0 7H6" />
        </svg>
      )
    case 'update':
      return (
        <svg {...common}>
          <path d="M21 12a9 9 0 1 1-2.6-6.3" />
          <polyline points="21 3 21 9 15 9" />
        </svg>
      )
    case 'report':
      return (
        <svg {...common}>
          <line x1="6" y1="20" x2="6" y2="10" />
          <line x1="12" y1="20" x2="12" y2="4" />
          <line x1="18" y1="20" x2="18" y2="14" />
        </svg>
      )
    case 'staff':
      return (
        <svg {...common}>
          <circle cx="9" cy="8" r="3.5" />
          <circle cx="17" cy="9" r="2.5" />
          <path d="M2 20v-1a5 5 0 0 1 10 0v1" />
          <path d="M14 20v-.5a3.5 3.5 0 0 1 5.5-2.9" />
        </svg>
      )
    case 'tg':
      return (
        <svg {...common}>
          <path d="M21 5 2.5 12.5l5.2 1.9L18 8l-8.2 8.4 1.1 5.1L14 17.5 19 21 21 5z" />
        </svg>
      )
    case 'display':
      return (
        <svg {...common}>
          <rect x="2" y="3" width="20" height="14" rx="2" />
          <line x1="8" y1="21" x2="16" y2="21" />
          <line x1="12" y1="17" x2="12" y2="21" />
        </svg>
      )
    case 'key':
      return (
        <svg {...common}>
          <circle cx="8" cy="15" r="4" />
          <path d="M10.8 12.2 20 3l1.5 1.5-1.5 1.5 1.5 1.5-2 2-1.5-1.5-3.2 3.2" />
        </svg>
      )
    case 'brush':
      return (
        <svg {...common}>
          <path d="M3 21c2.5 0 4-1.5 4-4l-2-2c-1.5 0-3 1.5-3 3 0 1.5.5 3 1 3z" />
          <path d="M7 15 18 4l2 2L9 17" />
        </svg>
      )
    case 'save':
      return (
        <svg {...common}>
          <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" />
          <polyline points="17 21 17 13 7 13 7 21" />
          <polyline points="7 3 7 8 15 8" />
        </svg>
      )
    case 'import':
      return (
        <svg {...common}>
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
          <polyline points="7 10 12 15 17 10" />
          <line x1="12" y1="15" x2="12" y2="3" />
        </svg>
      )
    case 'settings':
      return (
        <svg {...common}>
          <circle cx="12" cy="12" r="3" />
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-1.08-1.51 1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09a1.65 1.65 0 0 0 1.51-1.08 1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1.08 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9c.26.6.84 1 1.51 1.08H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1.08z" />
        </svg>
      )
    default:
      return <span className="nav-icon-dot" />
  }
}

function pageTitle(pathname: string): string {
  if (pathname === '/' || pathname === '') return 'Главная'
  for (const group of NAV) {
    for (const item of group.items) {
      if (item.to === '/') continue
      if (pathname === item.to || pathname.startsWith(`${item.to}/`)) return item.label
    }
  }
  return 'SHIFT Club'
}

export function AppLayout() {
  const navigate = useNavigate()
  const location = useLocation()
  const employee = JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as EmployeeDto | null
  const wide = location.pathname === '/floor'
  const [navOpen, setNavOpen] = useState(false)
  const [passwordOpen, setPasswordOpen] = useState(false)
  const title = useMemo(() => pageTitle(location.pathname), [location.pathname])
  const license = useLicenseStatus()
  // Пока лицензия не загрузилась, показываем все разделы: иначе меню мигает при входе.
  const licensedFeatures: readonly string[] = license.data?.features ?? ALL_FEATURES

  useEffect(() => {
    setNavOpen(false)
  }, [location.pathname])

  useEffect(() => {
    if (!navOpen) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setNavOpen(false)
    }
    document.body.classList.add('nav-drawer-open')
    window.addEventListener('keydown', onKey)
    return () => {
      document.body.classList.remove('nav-drawer-open')
      window.removeEventListener('keydown', onKey)
    }
  }, [navOpen])

  function logout() {
    setToken(null)
    localStorage.removeItem('shiftclub.employee')
    navigate('/login')
  }

  useDisableBrowserAutofill(true)

  return (
    <div className={`app-frame${navOpen ? ' app-frame--nav-open' : ''}`}>
      <form className="autofill-decoy" autoComplete="off" aria-hidden="true" tabIndex={-1}>
        <input type="text" name="username" autoComplete="username" tabIndex={-1} />
        <input type="password" name="password" autoComplete="current-password" tabIndex={-1} />
      </form>
      <StaffNotifications />

      <button
        type="button"
        className={`app-nav-backdrop${navOpen ? ' is-open' : ''}`}
        aria-label="Закрыть меню"
        tabIndex={navOpen ? 0 : -1}
        onClick={() => setNavOpen(false)}
      />

      <aside id="app-sidebar" className={`app-sidebar${navOpen ? ' is-open' : ''}`}>
        <div className="app-sidebar-brand">
          <p className="brand">SHIFT Club</p>
          <p className="sidebar-sub muted">Панель смены</p>
        </div>

        <nav className="app-nav">
          {NAV.map((group) => {
            const items = group.items.filter((item) => {
              const perm = 'perm' in item ? item.perm : undefined
              if (perm && !can(perm)) return false
              const feature = 'feature' in item ? item.feature : undefined
              return !feature || licensedFeatures.includes(feature)
            })
            if (items.length === 0) return null
            return (
            <div key={group.title} className="nav-group">
              <p className="nav-group-title">{group.title}</p>
              {items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={'end' in item ? item.end : false}
                  className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
                  onClick={() => setNavOpen(false)}
                >
                  <span className="nav-item-icon">
                    <NavIcon name={item.icon} />
                  </span>
                  <span className="nav-item-text">
                    <span className="nav-item-label">{item.label}</span>
                    <span className="nav-item-hint">{item.hint}</span>
                  </span>
                </NavLink>
              ))}
              {group.title === 'Смена' && (
                <a className="nav-item" href="/display" target="_blank" rel="noreferrer" title="Второй монитор">
                  <span className="nav-item-icon">
                    <NavIcon name="display" />
                  </span>
                  <span className="nav-item-text">
                    <span className="nav-item-label">Экран зала</span>
                    <span className="nav-item-hint">Второй монитор ↗</span>
                  </span>
                </a>
              )}
            </div>
            )
          })}
        </nav>

        <div className="app-sidebar-foot">
          <div className="user-pill">
            <strong>{employee?.displayName ?? employee?.login ?? 'Сотрудник'}</strong>
            <span className="muted">{rolesLabel(employee?.roles) || 'кассир'}</span>
          </div>
          <button type="button" className="ghost full" onClick={() => setPasswordOpen(true)}>
            Сменить пароль
          </button>
          <button type="button" className="ghost full" onClick={logout}>
            Выйти
          </button>
        </div>
      </aside>

      <ChangePasswordDialog open={passwordOpen} onClose={() => setPasswordOpen(false)} />

      <div className="app-shell">
        <header className="app-mobile-bar">
          <button
            type="button"
            className="app-menu-btn"
            aria-label={navOpen ? 'Закрыть меню' : 'Открыть меню'}
            aria-expanded={navOpen}
            aria-controls="app-sidebar"
            onClick={() => setNavOpen((v) => !v)}
          >
            <span className="app-menu-btn__bars" aria-hidden />
          </button>
          <div className="app-mobile-bar__title">
            <p className="brand app-mobile-brand">SHIFT</p>
            <strong>{title}</strong>
          </div>
          <span className="app-mobile-bar__user muted" title={employee?.displayName ?? employee?.login ?? ''}>
            {(employee?.displayName ?? employee?.login ?? 'Сотрудник').split(' ')[0]}
          </span>
        </header>

        <div className="app-content">
          <main className={wide ? 'app-main app-main--wide' : 'app-main'}>
            <LicenseBanner />
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  )
}
