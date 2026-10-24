export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    credentials: 'include',
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
  })
  const body = (await res.json().catch(() => ({}))) as { ok?: boolean; data?: T; error?: string }
  if (!res.ok || body.ok === false) throw new Error(body.error || 'Не получилось')
  return body.data as T
}
