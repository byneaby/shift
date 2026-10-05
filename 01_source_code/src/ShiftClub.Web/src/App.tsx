import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { getToken } from './api/client'
import { AppLayout } from './layout/AppLayout'
import { RequirePerm } from './RequirePerm'
import { DashboardPage } from './pages/DashboardPage'
import { FloorMapPage } from './pages/FloorMapPage'
import { CashPage } from './pages/CashPage'
import { BarPage } from './pages/BarPage'
import { CustomersPage } from './pages/CustomersPage'
import { BookingsPage } from './pages/BookingsPage'
import { ReportsPage } from './pages/ReportsPage'
import { EmployeesPage } from './pages/EmployeesPage'
import { SoftwareAppsPage } from './pages/SoftwareAppsPage'
import { ClientUpdatesPage } from './pages/ClientUpdatesPage'
import { TariffsPage } from './pages/TariffsPage'
import { ZonesPage } from './pages/ZonesPage'
import { FloorDisplayPage } from './pages/FloorDisplayPage'
import { ComputersPage } from './pages/ComputersPage'
import { MonitoringPage } from './pages/MonitoringPage'
import { NewsPage } from './pages/NewsPage'
import { TelegramSettingsPage } from './pages/TelegramSettingsPage'
import SettingsPage from './pages/SettingsPage'
import { LicensePage } from './pages/LicensePage'
import { CasesPage } from './pages/CasesPage'
import { WikiPage } from './pages/WikiPage'
import { LoginPage } from './pages/LoginPage'
import { Perm } from './permissions'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

function PrivateRoute({ children }: { children: ReactNode }) {
  return getToken() ? <>{children}</> : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            path="/display"
            element={
              <PrivateRoute>
                <FloorDisplayPage />
              </PrivateRoute>
            }
          />
          <Route
            element={
              <PrivateRoute>
                <AppLayout />
              </PrivateRoute>
            }
          >
            <Route path="/" element={<DashboardPage />} />
            <Route path="floor" element={<FloorMapPage />} />
            <Route path="wiki" element={<WikiPage />} />
            <Route path="wiki/:slug" element={<WikiPage />} />
            <Route
              path="computers"
              element={
                <RequirePerm anyOf={[Perm.ComputersManage]}>
                  <ComputersPage />
                </RequirePerm>
              }
            />
            <Route
              path="monitoring"
              element={
                <RequirePerm anyOf={[Perm.ComputersManage]}>
                  <MonitoringPage />
                </RequirePerm>
              }
            />
            <Route
              path="cash"
              element={
                <RequirePerm anyOf={[Perm.CashView]}>
                  <CashPage />
                </RequirePerm>
              }
            />
            <Route
              path="bar"
              element={
                <RequirePerm anyOf={[Perm.BarView, Perm.BarSell, Perm.BarOrders, Perm.InventoryManage]}>
                  <BarPage />
                </RequirePerm>
              }
            />
            <Route
              path="bookings"
              element={
                <RequirePerm anyOf={[Perm.BookingsView]}>
                  <BookingsPage />
                </RequirePerm>
              }
            />
            <Route
              path="customers"
              element={
                <RequirePerm anyOf={[Perm.CustomersView]}>
                  <CustomersPage />
                </RequirePerm>
              }
            />
            <Route
              path="cases"
              element={
                <RequirePerm anyOf={[Perm.CustomersView, Perm.SettingsManage]}>
                  <CasesPage />
                </RequirePerm>
              }
            />
            <Route
              path="news"
              element={
                <RequirePerm anyOf={[Perm.SettingsManage]}>
                  <NewsPage />
                </RequirePerm>
              }
            />
            <Route
              path="reports"
              element={
                <RequirePerm anyOf={[Perm.ReportsView]}>
                  <ReportsPage />
                </RequirePerm>
              }
            />
            <Route
              path="employees"
              element={
                <RequirePerm anyOf={[Perm.EmployeesManage]}>
                  <EmployeesPage />
                </RequirePerm>
              }
            />
            <Route
              path="telegram"
              element={
                <RequirePerm anyOf={[Perm.SettingsManage]}>
                  <TelegramSettingsPage />
                </RequirePerm>
              }
            />
            <Route
              path="software"
              element={
                <RequirePerm anyOf={[Perm.ComputersView, Perm.SettingsManage]}>
                  <SoftwareAppsPage />
                </RequirePerm>
              }
            />
            <Route
              path="updates"
              element={
                <RequirePerm anyOf={[Perm.SettingsManage]}>
                  <ClientUpdatesPage />
                </RequirePerm>
              }
            />
            <Route
              path="tariffs"
              element={
                <RequirePerm anyOf={[Perm.TariffsManage]}>
                  <TariffsPage />
                </RequirePerm>
              }
            />
            <Route
              path="zones"
              element={
                <RequirePerm anyOf={[Perm.ZonesManage]}>
                  <ZonesPage />
                </RequirePerm>
              }
            />
            <Route
              path="settings"
              element={
                <RequirePerm anyOf={[Perm.SettingsManage]}>
                  <SettingsPage />
                </RequirePerm>
              }
            />
            <Route
              path="license"
              element={
                <RequirePerm anyOf={[Perm.SettingsManage]}>
                  <LicensePage />
                </RequirePerm>
              }
            />
          </Route>
          <Route
            element={
              <PrivateRoute>
                <AppLayout />
              </PrivateRoute>
            }
          >
            <Route
              path="/telegram"
              element={
                <RequirePerm anyOf={[Perm.SettingsManage]}>
                  <TelegramSettingsPage />
                </RequirePerm>
              }
            />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
