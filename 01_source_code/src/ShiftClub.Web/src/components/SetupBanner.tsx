import { useQuery } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router-dom'
import { apiFetch } from '../api/client'
import type { SetupStatusDto } from '../api/client'
import { can } from '../permissions'

/**
 * Напоминание о незаконченной настройке клуба.
 * Видно только тем, кто может её закончить: в чек-листе написано, например,
 * что пароль владельца остался из поставки, — кассиру это знать не нужно.
 */
export function SetupBanner() {
  const location = useLocation()
  const allowed = can('settings.manage')

  const { data } = useQuery({
    queryKey: ['setup'],
    queryFn: async () => (await apiFetch<SetupStatusDto>('/api/setup')).data ?? null,
    enabled: allowed,
    staleTime: 60_000,
    retry: false,
  })

  if (!allowed || !data) return null
  if (data.completed || !data.required) return null
  if (location.pathname === '/setup') return null

  return (
    <div className="license-banner" role="status">
      <span>{data.summary}</span>
      <Link to="/setup">Закончить настройку</Link>
    </div>
  )
}
