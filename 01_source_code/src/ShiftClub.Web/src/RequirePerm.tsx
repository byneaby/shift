import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { can, type PermissionCode } from './permissions'
import { useLicenseStatus, type LicenseFeatureCode } from './licensing'

/** Route guard: any of the permission codes, or owner. */
export function RequirePerm({
  anyOf,
  children,
  fallback = '/',
}: {
  anyOf: PermissionCode[]
  children: ReactNode
  fallback?: string
}) {
  if (anyOf.length === 0 || can(...anyOf)) return <>{children}</>
  return <Navigate to={fallback} replace />
}

/** Route guard: возможность должна входить в лицензию клуба. */
export function RequireFeature({
  feature,
  children,
  fallback = '/',
}: {
  feature: LicenseFeatureCode
  children: ReactNode
  fallback?: string
}) {
  const { data, isLoading } = useLicenseStatus()

  // Не выкидываем со страницы, пока состояние лицензии ещё грузится.
  if (isLoading || !data) return <>{children}</>
  if (data.features.includes(feature)) return <>{children}</>
  return <Navigate to={fallback} replace />
}

export function AccessDenied({ title = 'Нет доступа' }: { title?: string }) {
  return (
    <section className="list-card" style={{ margin: 24 }}>
      <h1 style={{ marginTop: 0 }}>{title}</h1>
      <p className="muted">Недостаточно прав для этого раздела.</p>
    </section>
  )
}
