import { useEffect, useState, type ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { api } from './api'
import { AdminPage } from './pages/AdminPage'
import { DeskPage } from './pages/DeskPage'
import { Landing } from './pages/Landing'
import { LoginPage } from './pages/LoginPage'
import { SpinPage } from './pages/SpinPage'
import type { Me } from './types'

export function App() {
  const [me, setMe] = useState<Me | null | undefined>(undefined)

  useEffect(() => {
    api<Me>('/api/me').then(setMe).catch(() => setMe(null))
  }, [])

  return (
    <Routes>
      <Route path="/" element={<Landing me={me} />} />
      <Route path="/login" element={<LoginPage me={me} onLogin={setMe} />} />
      <Route path="/c/:slug" element={<SpinPage />} />
      <Route path="/c/:slug/:caseSlug" element={<SpinPage />} />
      <Route
        path="/admin"
        element={
          <Guard me={me} role="admin">
            <AdminPage me={me as Me} onLogout={() => setMe(null)} />
          </Guard>
        }
      />
      <Route
        path="/desk"
        element={
          <Guard me={me} role="club">
            <DeskPage me={me as Me} onLogout={() => setMe(null)} />
          </Guard>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

function Guard({ me, role, children }: { me: Me | null | undefined; role: Me['role']; children: ReactNode }) {
  if (me === undefined) return <p className="boot">Открываем кабинет…</p>
  if (!me) return <Navigate to="/login" replace />
  if (me.role !== role) return <Navigate to={me.role === 'admin' ? '/admin' : '/desk'} replace />
  return children
}
