/** Deep-link helpers for staff floor focus from toasts / URL. */
export type StaffFloorFocus = {
  focus?: 'bar' | 'help' | 'security' | null
  computerId?: string | null
  orderId?: string | null
}

export function parseStaffFloorSearch(search: string): StaffFloorFocus {
  const q = new URLSearchParams(search.startsWith('?') ? search.slice(1) : search)
  const focusRaw = q.get('focus')
  const focus =
    focusRaw === 'bar' || focusRaw === 'help' || focusRaw === 'security' ? focusRaw : null
  return {
    focus,
    computerId: q.get('computerId') || q.get('pc') || null,
    orderId: q.get('orderId') || q.get('order') || null,
  }
}

export function buildFloorFocusHref(opts: StaffFloorFocus): string {
  const q = new URLSearchParams()
  if (opts.focus) q.set('focus', opts.focus)
  if (opts.computerId) q.set('computerId', opts.computerId)
  if (opts.orderId) q.set('orderId', opts.orderId)
  const s = q.toString()
  return s ? `/floor?${s}` : '/floor'
}
