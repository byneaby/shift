import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { apiFetch, newIdempotencyKey } from '../api/client'
import { buildPcDisplay } from '../occupancy'

type ComputerDto = {
  id: string
  displayName?: string
  windowsName: string
  zoneName?: string
  status: string | number
  isApproved: boolean
  clientVersion?: string
  lastSeenAt?: string
  occupancy?: string
  occupancyDetail?: string
  currentSessionId?: string
  remainingSeconds?: number
  sessionStatus?: string | number
}

type CommandDto = {
  id: string
  status: string | number
  resultJson?: string | null
  errorMessage?: string | null
}

type TaskProcess = {
  pid: number
  name: string
  memoryMb: number
  windowTitle?: string
  canKill: boolean
}

type TelemetrySnapshot = {
  capturedAt?: string
  cpuLoadPercent?: number | null
  ramUsedPercent?: number | null
  ramUsedMb?: number | null
  ramTotalMb?: number | null
  freeDiskMb?: number | null
  cpuTempC?: number | null
  gpuTempC?: number | null
  gpuName?: string | null
  gpuLoadPercent?: number | null
  foregroundProcess?: string | null
  foregroundTitle?: string | null
  processes: TaskProcess[]
}

function isCommandDone(status: string | number) {
  const s = String(status)
  return s === 'Completed' || s === 'Failed' || s === 'Expired' || s === 'Cancelled' || s === '4' || s === '5' || s === '6' || s === '7'
}

function isCommandFailed(status: string | number) {
  const s = String(status)
  return s === 'Failed' || s === 'Expired' || s === 'Cancelled' || s === '5' || s === '6' || s === '7'
}

async function waitCommandResult(computerId: string, commandId: string, timeoutMs = 25000): Promise<CommandDto> {
  const start = Date.now()
  while (Date.now() - start < timeoutMs) {
    const res = await apiFetch<CommandDto>(`/api/computers/${computerId}/commands/${commandId}`)
    const cmd = res.data
    if (!cmd) throw new Error('Команда не найдена')
    if (isCommandDone(cmd.status)) {
      if (isCommandFailed(cmd.status)) {
        throw new Error(cmd.errorMessage || 'Команда не выполнена')
      }
      return cmd
    }
    await new Promise((r) => setTimeout(r, 400))
  }
  throw new Error('ПК не ответил вовремя (нужен Shell 0.6.70+)')
}

function parseTelemetry(resultJson?: string | null): TelemetrySnapshot {
  if (!resultJson) return { processes: [] }
  try {
    const raw = JSON.parse(resultJson) as Record<string, unknown>
    const list = (raw.processes ?? raw.Processes ?? []) as Record<string, unknown>[]
    const processes: TaskProcess[] = list.map((p) => ({
      pid: Number(p.pid ?? p.Pid),
      name: String(p.name ?? p.Name ?? '?'),
      memoryMb: Number(p.memoryMb ?? p.MemoryMb ?? 0),
      windowTitle: (p.windowTitle ?? p.WindowTitle) as string | undefined,
      canKill: Boolean(p.canKill ?? p.CanKill),
    }))
    const num = (a: unknown, b: unknown) => {
      const v = a ?? b
      return v == null || v === '' ? null : Number(v)
    }
    const str = (a: unknown, b: unknown) => {
      const v = a ?? b
      return v == null || v === '' ? null : String(v)
    }
    return {
      capturedAt: str(raw.capturedAt, raw.CapturedAt) ?? undefined,
      cpuLoadPercent: num(raw.cpuLoadPercent, raw.CpuLoadPercent),
      ramUsedPercent: num(raw.ramUsedPercent, raw.RamUsedPercent),
      ramUsedMb: num(raw.ramUsedMb, raw.RamUsedMb),
      ramTotalMb: num(raw.ramTotalMb, raw.RamTotalMb),
      freeDiskMb: num(raw.freeDiskMb, raw.FreeDiskMb),
      cpuTempC: num(raw.cpuTempC, raw.CpuTempC),
      gpuTempC: num(raw.gpuTempC, raw.GpuTempC),
      gpuName: str(raw.gpuName, raw.GpuName),
      gpuLoadPercent: num(raw.gpuLoadPercent, raw.GpuLoadPercent),
      foregroundProcess: str(raw.foregroundProcess, raw.ForegroundProcess),
      foregroundTitle: str(raw.foregroundTitle, raw.ForegroundTitle),
      processes,
    }
  } catch {
    return { processes: [] }
  }
}

function fmtPct(v?: number | null) {
  if (v == null || Number.isNaN(v)) return '—'
  return `${Math.round(v)}%`
}

function fmtMb(v?: number | null) {
  if (v == null || Number.isNaN(v)) return '—'
  if (v >= 1024) return `${(v / 1024).toFixed(1)} ГБ`
  return `${Math.round(v)} МБ`
}

function fmtTemp(v?: number | null) {
  if (v == null || Number.isNaN(v)) return '—'
  return `${Math.round(v)}°C`
}

export function MonitoringPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [snap, setSnap] = useState<TelemetrySnapshot | null>(null)
  const [capturedAt, setCapturedAt] = useState<string | null>(null)
  const [procFilter, setProcFilter] = useState('')

  const computersQuery = useQuery({
    queryKey: ['computers'],
    queryFn: async () => (await apiFetch<ComputerDto[]>('/api/computers')).data ?? [],
    refetchInterval: 12_000,
  })

  const pcs = useMemo(() => {
    return (computersQuery.data ?? [])
      .filter((c) => c.isApproved)
      .sort((a, b) => (a.displayName ?? a.windowsName).localeCompare(b.displayName ?? b.windowsName, 'ru'))
  }, [computersQuery.data])

  const selected = pcs.find((c) => c.id === selectedId) ?? null

  const filteredProcs = useMemo(() => {
    const list = snap?.processes ?? []
    const q = procFilter.trim().toLowerCase()
    if (!q) return list.slice(0, 40)
    return list
      .filter(
        (p) =>
          p.name.toLowerCase().includes(q) ||
          String(p.pid).includes(q) ||
          (p.windowTitle?.toLowerCase().includes(q) ?? false),
      )
      .slice(0, 40)
  }, [snap, procFilter])

  async function requestSnapshot(computerId: string) {
    setSelectedId(computerId)
    setLoading(true)
    setError(null)
    try {
      const sent = await apiFetch<CommandDto>(`/api/computers/${computerId}/commands`, {
        method: 'POST',
        body: JSON.stringify({
          type: 'GetDiagnostics',
          payloadJson: null,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
      if (!sent.data?.id) throw new Error('Не удалось отправить команду')
      const done = await waitCommandResult(computerId, sent.data.id)
      const parsed = parseTelemetry(done.resultJson)
      setSnap(parsed)
      setCapturedAt(new Date().toLocaleTimeString('ru-RU'))
      if (
        parsed.cpuLoadPercent == null &&
        parsed.ramUsedPercent == null &&
        parsed.processes.length === 0
      ) {
        setError('Пустой ответ — обновите Shell до 0.6.70+')
      }
    } catch (e) {
      setSnap(null)
      setError(e instanceof Error ? e.message : 'Ошибка запроса')
    } finally {
      setLoading(false)
    }
  }

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Мониторинг</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            CPU, GPU, RAM и процессы — только по запросу, без постоянной нагрузки на ПК
          </p>
        </div>
      </header>

      {computersQuery.isError && <p className="error">{(computersQuery.error as Error).message}</p>}

      <div className="mon-layout">
        <section className="panel mon-list">
          <div className="mon-list-head">
            <strong>ПК в зале</strong>
            <span className="muted" style={{ fontSize: 12 }}>
              {pcs.length} шт.
            </span>
          </div>
          <div className="mon-pc-scroll">
            {pcs.map((c) => {
              const view = buildPcDisplay(c)
              const active = c.id === selectedId
              return (
                <button
                  key={c.id}
                  type="button"
                  className={`mon-pc-row${active ? ' active' : ''}`}
                  disabled={loading}
                  onClick={() => void requestSnapshot(c.id)}
                >
                  <span className="mon-pc-dot" style={{ background: view.color }} />
                  <span className="mon-pc-meta">
                    <span className="mon-pc-name">{c.displayName ?? c.windowsName}</span>
                    <span className="muted" style={{ fontSize: 12 }}>
                      {view.statusLabel}
                      {c.zoneName ? ` · ${c.zoneName}` : ''}
                      {view.offlineBadge ? ' · офф' : ''}
                    </span>
                  </span>
                  <span className="mon-pc-action muted">{active && loading ? '…' : 'Запрос'}</span>
                </button>
              )
            })}
            {pcs.length === 0 && !computersQuery.isLoading && (
              <p className="muted" style={{ padding: 12, margin: 0 }}>
                Нет подтверждённых ПК
              </p>
            )}
          </div>
        </section>

        <section className="panel mon-detail">
          {!selected && (
            <div className="mon-empty">
              <p>Выберите ПК слева — снимок соберётся один раз.</p>
              <p className="muted" style={{ fontSize: 13 }}>
                Повторный опрос только по кнопке «Обновить снимок».
              </p>
            </div>
          )}

          {selected && (
            <>
              <div className="mon-detail-head">
                <div>
                  <h2 style={{ margin: 0, fontSize: 18 }}>{selected.displayName ?? selected.windowsName}</h2>
                  <p className="muted" style={{ margin: '4px 0 0', fontSize: 13 }}>
                    {selected.clientVersion ? `Shell ${selected.clientVersion}` : 'версия Shell неизвестна'}
                    {capturedAt ? ` · снимок ${capturedAt}` : ''}
                  </p>
                </div>
                <button
                  type="button"
                  className="primary"
                  disabled={loading}
                  onClick={() => void requestSnapshot(selected.id)}
                >
                  {loading ? 'Запрос…' : 'Обновить снимок'}
                </button>
              </div>

              {error && <p className="error">{error}</p>}

              {snap && (
                <>
                  <div className="mon-metrics">
                    <div className="mon-metric">
                      <span className="muted">CPU</span>
                      <strong>{fmtPct(snap.cpuLoadPercent)}</strong>
                      <span className="muted" style={{ fontSize: 12 }}>
                        {snap.cpuTempC != null ? fmtTemp(snap.cpuTempC) : 'темп. CPU недоступна'}
                      </span>
                    </div>
                    <div className="mon-metric">
                      <span className="muted">RAM</span>
                      <strong>{fmtPct(snap.ramUsedPercent)}</strong>
                      <span className="muted" style={{ fontSize: 12 }}>
                        {fmtMb(snap.ramUsedMb)} / {fmtMb(snap.ramTotalMb)}
                      </span>
                    </div>
                    <div className="mon-metric">
                      <span className="muted">GPU</span>
                      <strong>
                        {snap.gpuLoadPercent != null ? fmtPct(snap.gpuLoadPercent) : '—'}
                      </strong>
                      <span className="muted" style={{ fontSize: 12 }}>
                        {snap.gpuTempC != null ? fmtTemp(snap.gpuTempC) : 'темп. н/д'}
                        {snap.gpuName ? ` · ${snap.gpuName}` : ''}
                      </span>
                    </div>
                    <div className="mon-metric">
                      <span className="muted">Диск свободно</span>
                      <strong>{fmtMb(snap.freeDiskMb)}</strong>
                      <span className="muted" style={{ fontSize: 12 }}>
                        все локальные тома
                      </span>
                    </div>
                  </div>

                  <div className="mon-fg">
                    <span className="muted">На экране</span>
                    <strong>
                      {snap.foregroundProcess ?? '—'}
                      {snap.foregroundTitle ? ` — ${snap.foregroundTitle}` : ''}
                    </strong>
                  </div>

                  <div className="mon-procs-head">
                    <strong>Процессы (топ по RAM)</strong>
                    <input
                      className="input"
                      placeholder="Фильтр…"
                      value={procFilter}
                      onChange={(e) => setProcFilter(e.target.value)}
                      style={{ maxWidth: 200 }}
                    />
                  </div>
                  <div className="mon-procs-table-wrap">
                    <table className="table mon-procs-table">
                      <thead>
                        <tr>
                          <th>Имя</th>
                          <th>PID</th>
                          <th>RAM</th>
                          <th>Окно</th>
                        </tr>
                      </thead>
                      <tbody>
                        {filteredProcs.map((p) => (
                          <tr key={`${p.pid}-${p.name}`}>
                            <td>{p.name}</td>
                            <td className="muted">{p.pid}</td>
                            <td>{p.memoryMb} МБ</td>
                            <td className="muted">{p.windowTitle ?? '—'}</td>
                          </tr>
                        ))}
                        {filteredProcs.length === 0 && (
                          <tr>
                            <td colSpan={4} className="muted">
                              Нет процессов в выборке
                            </td>
                          </tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </>
              )}

              {!snap && !loading && !error && (
                <p className="muted">Нажмите «Обновить снимок», чтобы запросить данные с ПК.</p>
              )}
            </>
          )}
        </section>
      </div>
    </>
  )
}
