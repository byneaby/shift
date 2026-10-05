import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import type { LicenseStatusDto } from '../api/client'
import { can } from '../permissions'

/**
 * Полоса предупреждения о лицензии над содержимым панели.
 * Видна всем сотрудникам: кассир должен понимать, почему не стартует сеанс.
 * Ссылка на раздел — только тем, кто может что-то сделать.
 */
export function LicenseBanner() {
  const { data } = useQuery({
    queryKey: ['license'],
    queryFn: async () => (await apiFetch<LicenseStatusDto>('/api/license')).data ?? null,
    staleTime: 60_000,
    refetchInterval: 5 * 60_000,
    retry: false,
  })

  if (!data?.warning) return null

  // «Нет ключа» без принудительного режима — это нормальная работа своего клуба, не пугаем сменой.
  const silentMissing = data.state === 'Missing' && data.canStartSessions
  if (silentMissing) return null

  const isWarn = data.state === 'Active'

  return (
    <div className={`license-banner${isWarn ? ' license-banner--warn' : ''}`} role="status">
      <span>{data.warning}</span>
      {can('settings.manage') && <Link to="/license">Открыть раздел «Лицензия»</Link>}
    </div>
  )
}
