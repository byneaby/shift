import { useEffect } from 'react'

const SKIP = '[data-allow-autocomplete]'

function hardenField(el: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement) {
  if (el.closest(SKIP)) return
  if ('type' in el && (el.type === 'hidden' || el.type === 'checkbox' || el.type === 'radio' || el.type === 'file'))
    return

  const isPassword = 'type' in el && el.type === 'password'
  // "off" is ignored by Chrome on name/phone/email; unknown token blocks it.
  el.setAttribute('autocomplete', isPassword ? 'new-password' : 'nope')
  el.setAttribute('autocorrect', 'off')
  el.setAttribute('autocapitalize', 'none')
  el.setAttribute('data-lpignore', 'true')
  el.setAttribute('data-1p-ignore', 'true')
  el.setAttribute('data-bwignore', 'true')
  el.setAttribute('data-form-type', 'other')
}

function hardenTree(root: ParentNode) {
  root.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(
    'input, textarea, select',
  ).forEach(hardenField)
  if (root instanceof HTMLInputElement || root instanceof HTMLTextAreaElement || root instanceof HTMLSelectElement) {
    hardenField(root)
  }
}

/** Disable browser / password-manager autofill inside the staff panel. Login page is excluded. */
export function useDisableBrowserAutofill(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return
    const root = document.getElementById('root')
    if (!root) return
    hardenTree(root)
    const mo = new MutationObserver((records) => {
      for (const rec of records) {
        for (const node of rec.addedNodes) {
          if (node instanceof HTMLElement) hardenTree(node)
        }
      }
    })
    mo.observe(root, { childList: true, subtree: true })
    return () => mo.disconnect()
  }, [enabled])
}
