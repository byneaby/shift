export type ApiResponse<T> = {
  success: boolean
  data?: T
  errorCode?: string
  message?: string
  errors?: string[]
}

export type EmployeeDto = {
  id: string
  login: string
  displayName: string
  branchId?: string
  roles: string[]
  permissions: string[]
}

export type LoginResponse = {
  accessToken: string
  expiresAt: string
  employee: EmployeeDto
}

export type ZoneDto = {
  id: string
  branchId: string
  name: string
  code: string
  colorHex: string
  sortOrder: number
  minSessionMinutes?: number
  isActive: boolean
  kind?: string
  gridColumns?: number
  gridRows?: number
}

export type BranchDto = {
  id: string
  name: string
  code: string
  timeZoneId: string
  currencyCode: string
  isActive: boolean
  zones: ZoneDto[]
}

const TOKEN_KEY = 'shiftclub.token'

export function getToken() {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string | null) {
  if (token) localStorage.setItem(TOKEN_KEY, token)
  else localStorage.removeItem(TOKEN_KEY)
}

/** UUID для идемпотентности. Работает и по HTTP в LAN (не только localhost/HTTPS). */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto as Crypto | undefined
  if (c && typeof c.randomUUID === 'function') {
    return c.randomUUID()
  }
  if (c && typeof c.getRandomValues === 'function') {
    const bytes = new Uint8Array(16)
    c.getRandomValues(bytes)
    bytes[6] = (bytes[6] & 0x0f) | 0x40
    bytes[8] = (bytes[8] & 0x3f) | 0x80
    const hex = [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('')
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
  }
  return `id-${Date.now()}-${Math.random().toString(16).slice(2)}-${Math.random().toString(16).slice(2)}`
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<ApiResponse<T>> {
  const headers = new Headers(init?.headers)
  headers.set('Content-Type', 'application/json')
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(path, { ...init, headers })
  if (response.status === 401) {
    const isAuthAnonymous =
      path.includes('/api/auth/login') || path.includes('/api/auth/telegram')
    if (!isAuthAnonymous) {
      setToken(null)
      localStorage.removeItem('shiftclub.employee')
      if (!window.location.pathname.startsWith('/login')) {
        window.location.href = '/login'
      }
    }
    let message = isAuthAnonymous ? 'Неверный логин или пароль' : 'Сессия истекла — войдите снова'
    try {
      const payload401 = JSON.parse(await response.text()) as ApiResponse<T>
      if (payload401.message) message = payload401.message
    } catch {
      /* ignore */
    }
    throw new Error(message)
  }

  const raw = await response.text()
  let payload: ApiResponse<T>
  try {
    payload = JSON.parse(raw) as ApiResponse<T>
  } catch {
    throw new Error(raw.trim().slice(0, 180) || `Ошибка сервера (${response.status})`)
  }
  if (!response.ok || !payload.success) {
    throw new Error(payload.message ?? `Ошибка сервера (${response.status})`)
  }
  return payload
}

/** Multipart upload (do not set Content-Type — browser sets boundary). */
export async function apiUpload<T>(path: string, form: FormData): Promise<ApiResponse<T>> {
  const headers = new Headers()
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(path, { method: 'POST', headers, body: form })
  if (response.status === 401) {
    setToken(null)
    localStorage.removeItem('shiftclub.employee')
    if (!window.location.pathname.startsWith('/login')) {
      window.location.href = '/login'
    }
    throw new Error('Сессия истекла — войдите снова')
  }

  const raw = await response.text()
  let payload: ApiResponse<T>
  try {
    payload = JSON.parse(raw) as ApiResponse<T>
  } catch {
    throw new Error(raw.trim().slice(0, 180) || `Ошибка сервера (${response.status})`)
  }
  if (!response.ok || !payload.success) {
    throw new Error(payload.message ?? `Ошибка сервера (${response.status})`)
  }
  return payload
}

/** Скачивание CSV/бинарных ответов с JWT (не JSON ApiResponse). */
export async function downloadAuthenticated(path: string, filename: string) {
  const headers = new Headers()
  const token = getToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(path, { headers })
  if (!response.ok) {
    let message = `Ошибка сервера (${response.status})`
    try {
      const payload = (await response.json()) as ApiResponse<unknown>
      message = payload.message ?? message
    } catch {
      /* ignore */
    }
    throw new Error(message)
  }

  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

