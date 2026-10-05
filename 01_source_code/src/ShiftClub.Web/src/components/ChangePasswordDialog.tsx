import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { apiFetch } from '../api/client'
import { ActionDialog } from './ActionDialog'

type Props = {
  open: boolean
  onClose: () => void
}

export function ChangePasswordDialog({ open, onClose }: Props) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  function reset() {
    setCurrentPassword('')
    setNewPassword('')
    setConfirmPassword('')
    setError(null)
  }

  function handleClose() {
    if (mutation.isPending) return
    reset()
    onClose()
  }

  const mutation = useMutation({
    mutationFn: async () => {
      if (newPassword.trim().length < 6) throw new Error('Новый пароль — минимум 6 символов')
      if (newPassword !== confirmPassword) throw new Error('Пароли не совпадают')
      const res = await apiFetch('/api/auth/change-password', {
        method: 'POST',
        body: JSON.stringify({
          currentPassword,
          newPassword,
        }),
      })
      if (!res.success) throw new Error(res.message || 'Не удалось сменить пароль')
    },
    onSuccess: () => {
      reset()
      onClose()
      window.alert('Пароль изменён. При следующем входе используйте новый пароль.')
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка'),
  })

  return (
    <ActionDialog
      open={open}
      onClose={handleClose}
      title="Сменить пароль"
      size="sm"
      footer={
        <>
          <button type="button" className="ghost" onClick={handleClose} disabled={mutation.isPending}>
            Отмена
          </button>
          <button
            type="button"
            disabled={mutation.isPending || !currentPassword || !newPassword || !confirmPassword}
            onClick={() => {
              setError(null)
              mutation.mutate()
            }}
          >
            Сохранить
          </button>
        </>
      }
    >
      <div className="form-stack">
        <p className="muted" style={{ margin: 0 }}>
          Смена пароля вашего аккаунта в панели. Текущая сессия останется активной.
        </p>
        <label>
          Текущий пароль
          <input
            type="password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            autoComplete="current-password"
          />
        </label>
        <label>
          Новый пароль
          <input
            type="password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            autoComplete="new-password"
          />
        </label>
        <label>
          Повтор нового пароля
          <input
            type="password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            autoComplete="new-password"
          />
        </label>
        {error && <p className="error">{error}</p>}
      </div>
    </ActionDialog>
  )
}
