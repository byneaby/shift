import { useEffect, type ReactNode } from 'react'

type ActionDialogProps = {
  open: boolean
  onClose: () => void
  title: string
  description?: string
  size?: 'sm' | 'md' | 'lg' | 'xl'
  children: ReactNode
  footer?: ReactNode
  closeOnBackdrop?: boolean
}

export function ActionDialog({
  open,
  onClose,
  title,
  description,
  size = 'md',
  children,
  footer,
  closeOnBackdrop = true,
}: ActionDialogProps) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = prev
    }
  }, [open, onClose])

  if (!open) return null

  return (
    <div
      className="dialog-backdrop"
      role="presentation"
      onMouseDown={(e) => {
        if (closeOnBackdrop && e.target === e.currentTarget) onClose()
      }}
    >
      <div
        className={`dialog dialog--${size}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby="dialog-title"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <form
          autoComplete="off"
          onSubmit={(e) => e.preventDefault()}
          style={{ display: 'contents' }}
        >
        <header className="dialog-header">
          <div>
            <h2 id="dialog-title">{title}</h2>
            {description && <p className="muted dialog-desc">{description}</p>}
          </div>
          <button type="button" className="ghost dialog-close" onClick={onClose} aria-label="Закрыть">
            ✕
          </button>
        </header>
        <div className="dialog-body">{children}</div>
        {footer && <footer className="dialog-footer">{footer}</footer>}
        </form>
      </div>
    </div>
  )
}

type ConfirmDialogProps = {
  open: boolean
  onClose: () => void
  title: string
  message: string
  confirmLabel?: string
  danger?: boolean
  loading?: boolean
  onConfirm: () => void
}

export function ConfirmDialog({
  open,
  onClose,
  title,
  message,
  confirmLabel = 'Подтвердить',
  danger,
  loading,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <ActionDialog
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      closeOnBackdrop={!loading}
      footer={
        <>
          <button type="button" className="ghost" onClick={onClose} disabled={loading}>
            Отмена
          </button>
          <button
            type="button"
            className={danger ? 'danger' : undefined}
            disabled={loading}
            onClick={onConfirm}
          >
            {confirmLabel}
          </button>
        </>
      }
    >
      <p className="dialog-message">{message}</p>
    </ActionDialog>
  )
}
