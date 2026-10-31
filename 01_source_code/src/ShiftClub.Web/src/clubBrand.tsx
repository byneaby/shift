import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'

export type ClubBrand = {
  name: string
  logoUrl: string | null
}

const EMPTY: ClubBrand = { name: '', logoUrl: null }

async function fetchClubBrand(): Promise<ClubBrand> {
  const response = await fetch('/api/client/branding', { headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error('brand')
  const body = (await response.json()) as { data?: { name?: string; logoUrl?: string | null } }
  const data = body.data ?? {}
  const name = String(data.name ?? '').trim()
  const logo = typeof data.logoUrl === 'string' && data.logoUrl.length > 0 ? data.logoUrl : null
  return { name, logoUrl: logo }
}

/** One request for the whole panel session. The logo file is cached by the browser. */
export function useClubBrand() {
  const query = useQuery({
    queryKey: ['club-brand'],
    queryFn: fetchClubBrand,
    staleTime: Number.POSITIVE_INFINITY,
    refetchOnMount: false,
    refetchOnReconnect: false,
    retry: 1,
  })
  const brand = query.data ?? EMPTY
  useEffect(() => {
    if (brand.name) document.title = brand.name
  }, [brand.name])
  return brand
}

export function ClubBrandMark({ className }: { className?: string }) {
  const brand = useClubBrand()
  return (
    <p className={className ? `brand ${className}` : 'brand'}>
      {brand.logoUrl ? <img className="brand-logo" src={brand.logoUrl} alt="" /> : null}
      <span>{brand.name}</span>
    </p>
  )
}
