import { apiFetch } from './api/client'

export type KaspiPosStatus = {
  configured: boolean
  registered: boolean
  ready: boolean
  host: string | null
  terminalId: string
  registerName: string | null
  message: string | null
  tokenExpiresAt: string | null
}

export type KaspiPosPaymentResult = {
  success: boolean
  status: string
  processId?: string | null
  transactionId?: string | null
  paymentMethod?: string | null
  subStatus?: string | null
  message?: string | null
}

let cachedStatus: KaspiPosStatus | null = null
let cachedAt = 0

export async function fetchKaspiPosStatus(force = false): Promise<KaspiPosStatus | null> {
  if (!force && cachedStatus && Date.now() - cachedAt < 60_000) return cachedStatus
  try {
    const res = await apiFetch<KaspiPosStatus>('/api/kaspi-pos/status')
    cachedStatus = res.data ?? null
    cachedAt = Date.now()
    return cachedStatus
  } catch {
    return null
  }
}

/** If terminal ready — charge on Smart POS first. If not ready — no-op (manual terminal). */
export async function payKaspiTerminalIfReady(amountKzt: number): Promise<KaspiPosPaymentResult | null> {
  const amount = Math.round(amountKzt)
  if (amount <= 0) return null

  const status = await fetchKaspiPosStatus()
  if (!status?.ready) return null

  const res = await apiFetch<KaspiPosPaymentResult>('/api/kaspi-pos/pay', {
    method: 'POST',
    body: JSON.stringify({ amount }),
  })

  const data = res.data
  if (!data?.success) {
    throw new Error(data?.message ?? 'Оплата на Kaspi POS не прошла')
  }

  return data
}

export function invalidateKaspiPosStatusCache() {
  cachedStatus = null
  cachedAt = 0
}
