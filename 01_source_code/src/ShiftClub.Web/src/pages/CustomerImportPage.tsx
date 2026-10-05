import { useMutation } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { apiFetch } from '../api/client'
import { money } from '../format'

type ImportRow = {
  lineNumber: number
  firstName: string
  lastName: string
  phone: string
  email?: string | null
  balance: number
  bonusBalance: number
  notes?: string | null
  action: 'create' | 'update' | 'skip'
  problem?: string | null
}

type Preview = {
  totalRows: number
  willCreate: number
  willUpdate: number
  willSkip: number
  balanceToPost: number
  recognizedColumns: string[]
  warnings: string[]
  rows: ImportRow[]
  summary: string
}

type ImportResult = {
  created: number
  updated: number
  skipped: number
  balancePosted: number
  summary: string
}

const ACTION_LABELS: Record<string, string> = {
  create: 'создать',
  update: 'обновить',
  skip: 'пропустить',
}

const SAMPLE = `Телефон;Имя;Фамилия;Баланс;Бонусы;Примечание
+7 701 123 45 67;Асет;Жумабек;1 500,50;200;постоянный
87012223344;Данияр;;0;0;
`

export function CustomerImportPage() {
  const fileInput = useRef<HTMLInputElement>(null)
  const [csv, setCsv] = useState('')
  const [fileName, setFileName] = useState<string | null>(null)
  const [updateExisting, setUpdateExisting] = useState(false)
  const [importBalances, setImportBalances] = useState(true)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const body = () => JSON.stringify({ csv, updateExisting, importBalances })

  const previewMutation = useMutation({
    mutationFn: async () => {
      if (!csv.trim()) throw new Error('Сначала выберите файл или вставьте текст')
      const res = await apiFetch<Preview>('/api/customers/import/preview', {
        method: 'POST',
        body: body(),
      })
      return res.data!
    },
    onSuccess: (data) => {
      setError(null)
      setResult(null)
      setPreview(data)
    },
    onError: (e) => {
      setPreview(null)
      setError(e instanceof Error ? e.message : 'Ошибка')
    },
  })

  const importMutation = useMutation({
    mutationFn: async () => {
      const res = await apiFetch<ImportResult>('/api/customers/import', {
        method: 'POST',
        body: body(),
      })
      return res.data!
    },
    onSuccess: (data) => {
      setError(null)
      setResult(data)
      setPreview(null)
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка'),
  })

  async function onFile(file: File | null) {
    if (!file) return
    setFileName(file.name)
    setPreview(null)
    setResult(null)
    setCsv(await file.text())
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Импорт клиентов</h1>
          <p className="muted">Перенос базы из старой системы: CSV с телефонами, именами и остатками</p>
        </div>
      </header>

      <div className="bar-layout">
        <section className="cash-card">
          <h2 className="section-title">Файл</h2>
          <div className="form-stack">
            <input
              ref={fileInput}
              type="file"
              accept=".csv,.txt,text/csv"
              onChange={(e) => void onFile(e.target.files?.[0] ?? null)}
            />
            {fileName && <p className="muted">Выбран: {fileName}</p>}

            <label>
              Или вставьте текст
              <textarea
                rows={7}
                spellCheck={false}
                value={csv}
                placeholder={SAMPLE}
                onChange={(e) => {
                  setCsv(e.target.value)
                  setPreview(null)
                  setResult(null)
                }}
              />
            </label>

            <label className="check-row">
              <input
                type="checkbox"
                checked={updateExisting}
                onChange={(e) => setUpdateExisting(e.target.checked)}
              />
              Обновлять тех, кто уже есть (сверка по телефону)
            </label>
            <label className="check-row">
              <input
                type="checkbox"
                checked={importBalances}
                onChange={(e) => setImportBalances(e.target.checked)}
              />
              Переносить остатки баланса и бонусов
            </label>

            <button
              type="button"
              disabled={previewMutation.isPending}
              onClick={() => previewMutation.mutate()}
            >
              {previewMutation.isPending ? 'Разбираю…' : 'Проверить файл'}
            </button>
            {error && <p className="error">{error}</p>}

            <p className="muted">
              Нужен заголовок со столбцом телефона: «Телефон» или «phone». Остальное — по желанию:
              имя, фамилия, email, баланс, бонусы, дата рождения, примечание. Разделитель — запятая,
              точка с запятой или табуляция, определяется сам.
            </p>
            <p className="muted">
              Остатки заводятся проводкой в историю операций клиента, а не записью в поле баланса —
              поэтому деньги в отчётах всегда имеют происхождение. Повторный импорт того же файла
              баланс не удвоит.
            </p>
          </div>
        </section>

        <section className="cash-card">
          <h2 className="section-title">Что получится</h2>

          {result && (
            <>
              <p className="flash">{result.summary}</p>
              <ul className="receipt-list">
                <li>
                  <span>Создано</span>
                  <strong className="ok">{result.created}</strong>
                </li>
                <li>
                  <span>Обновлено</span>
                  <strong>{result.updated}</strong>
                </li>
                <li>
                  <span>Пропущено</span>
                  <strong>{result.skipped}</strong>
                </li>
                <li>
                  <span>Перенесено балансов</span>
                  <strong>{money(result.balancePosted)}</strong>
                </li>
              </ul>
            </>
          )}

          {!preview && !result && <p className="muted">Проверьте файл — здесь появится разбор</p>}

          {preview && (
            <>
              <ul className="receipt-list">
                <li>
                  <span>Строк в файле</span>
                  <strong>{preview.totalRows}</strong>
                </li>
                <li>
                  <span>Будет создано</span>
                  <strong className="ok">{preview.willCreate}</strong>
                </li>
                <li>
                  <span>Будет обновлено</span>
                  <strong>{preview.willUpdate}</strong>
                </li>
                <li>
                  <span>Будет пропущено</span>
                  <strong className={preview.willSkip > 0 ? 'error' : undefined}>{preview.willSkip}</strong>
                </li>
                <li>
                  <span>Сумма переносимых остатков</span>
                  <strong>{money(preview.balanceToPost)}</strong>
                </li>
              </ul>

              {preview.recognizedColumns.length > 0 && (
                <p className="muted">Распознаны столбцы: {preview.recognizedColumns.join(', ')}</p>
              )}
              {preview.warnings.map((w) => (
                <p key={w} className="error">
                  {w}
                </p>
              ))}

              <h3 className="section-title">Строки</h3>
              <div className="reports-table-wrap" style={{ maxHeight: 420 }}>
                <table className="reports-table">
                  <thead>
                    <tr>
                      <th>Стр.</th>
                      <th>Клиент</th>
                      <th>Телефон</th>
                      <th>Баланс</th>
                      <th>Что будет</th>
                    </tr>
                  </thead>
                  <tbody>
                    {preview.rows.map((r) => (
                      <tr key={r.lineNumber}>
                        <td>{r.lineNumber}</td>
                        <td>{`${r.lastName} ${r.firstName}`.trim()}</td>
                        <td>{r.phone || '—'}</td>
                        <td>{money(r.balance)}</td>
                        <td className={r.action === 'skip' ? 'error' : 'ok'}>
                          {ACTION_LABELS[r.action] ?? r.action}
                          {r.problem && <div className="muted">{r.problem}</div>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {preview.willCreate + preview.willUpdate > 0 && (
                <button
                  type="button"
                  disabled={importMutation.isPending}
                  onClick={() => {
                    const msg =
                      `Импортировать? Создано будет ${preview.willCreate}, обновлено ${preview.willUpdate}.` +
                      (importBalances && preview.balanceToPost > 0
                        ? ` На балансы клиентов попадёт ${money(preview.balanceToPost)}.`
                        : '')
                    if (confirm(msg)) importMutation.mutate()
                  }}
                >
                  {importMutation.isPending ? 'Импортирую…' : 'Импортировать'}
                </button>
              )}
            </>
          )}
        </section>
      </div>
    </>
  )
}
