import { useQuery } from '@tanstack/react-query'
import { apiFetch } from './api/client'
import type { LicenseStatusDto } from './api/client'

export const LicenseFeature = {
  ShiftCase: 'shift_case',
  MultiBranch: 'multi_branch',
  TelegramCrm: 'telegram_crm',
  KaspiPos: 'kaspi_pos',
} as const

export type LicenseFeatureCode = (typeof LicenseFeature)[keyof typeof LicenseFeature] | string

/** Один запрос на всю панель: тот же ключ использует баннер лицензии. */
export function useLicenseStatus() {
  return useQuery({
    queryKey: ['license'],
    queryFn: async () => (await apiFetch<LicenseStatusDto>('/api/license')).data ?? null,
    staleTime: 60_000,
  })
}

/**
 * Пока состояние лицензии не загрузилось, считаем возможность включённой.
 * Иначе при каждом открытии панели разделы будут мигать и исчезать.
 */
export function useHasFeature(feature: LicenseFeatureCode): boolean {
  const { data, isLoading } = useLicenseStatus()
  if (isLoading || !data) return true
  return data.features.includes(feature)
}
