import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { apiFetch } from './api/client'

export type BrandingDto = {
  clubName: string
  shortName: string
  logoUrl?: string | null
  accentColor: string
  loginBackgroundUrl?: string | null
  websiteUrl?: string | null
  supportContact?: string | null
  telegramSignature: string
}

/** Пока название не пришло — не показываем чужой бренд и не мигаем текстом. */
export const BRANDING_PLACEHOLDER: BrandingDto = {
  clubName: 'Клуб',
  shortName: 'Клуб',
  logoUrl: null,
  accentColor: '#ff6a00',
  loginBackgroundUrl: null,
  websiteUrl: null,
  supportContact: null,
  telegramSignature: 'Клуб',
}

/** Ручка открыта без входа: нужна экрану логина и табло в зале. */
export function useBranding(): BrandingDto {
  const { data } = useQuery({
    queryKey: ['branding'],
    queryFn: async () => (await apiFetch<BrandingDto>('/api/branding')).data ?? null,
    staleTime: 5 * 60_000,
  })
  return data ?? BRANDING_PLACEHOLDER
}

/** Заголовок окна и акцентный цвет темы — из настроек клуба, а не из кода. */
export function useApplyBranding(branding: BrandingDto) {
  useEffect(() => {
    if (branding === BRANDING_PLACEHOLDER) return
    document.title = branding.clubName
    document.documentElement.style.setProperty('--accent', branding.accentColor)
  }, [branding])
}
