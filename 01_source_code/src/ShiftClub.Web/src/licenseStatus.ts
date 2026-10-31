import { useQuery } from '@tanstack/react-query'
import { apiFetch, getToken } from './api/client'

export type LicenseStatus = {
  enforce: boolean
  operational: boolean
  message: string
  club: string | null
  kind: string | null
  maxComputers: number | null
  usedComputers: number
  expiresAt: string | null
  daysRemaining: number | null
  warning: boolean
  refreshMinutes: number
}

export const licenseStatusKey = ['license-status'] as const

const ZONE = 'Asia/Qyzylorda'

export function useLicenseStatus() {
  return useQuery({
    queryKey: licenseStatusKey,
    queryFn: async () => {
      const result = await apiFetch<LicenseStatus>('/api/license/status')
      if (!result.data) throw new Error('Сервер не прислал состояние лицензии')
      return result.data
    },
    enabled: !!getToken(),
    refetchInterval: 60_000,
  })
}

export function formatLicenseWhen(iso: string | null | undefined): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('ru-KZ', {
    timeZone: ZONE,
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

export function daysPhrase(days: number): string {
  const n = Math.abs(Math.trunc(days))
  const mod100 = n % 100
  const mod10 = n % 10
  let word = 'дней'
  if (mod100 < 11 || mod100 > 14) {
    if (mod10 === 1) word = 'день'
    else if (mod10 >= 2 && mod10 <= 4) word = 'дня'
  }
  return `${n} ${word}`
}

export function kindLabel(kind: string | null | undefined): string {
  if (kind === 'trial') return 'Пробный период'
  if (kind === 'paid') return 'Оплачена'
  if (!kind) return '—'
  return kind
}

export function licenseTone(status: LicenseStatus): 'off' | 'bad' | 'warn' | 'ok' {
  if (!status.enforce) return 'off'
  if (!status.operational) return 'bad'
  if (status.warning) return 'warn'
  return 'ok'
}

export function licenseStateLabel(status: LicenseStatus): string {
  if (!status.enforce) return 'Проверка выключена'
  if (!status.operational) return 'Остановлена'
  if (status.warning) return 'Скоро закончится'
  return 'Работает'
}

export function seatsLabel(status: LicenseStatus): string {
  if (status.maxComputers == null) return 'без лимита'
  return `${status.usedComputers} из ${status.maxComputers}`
}
