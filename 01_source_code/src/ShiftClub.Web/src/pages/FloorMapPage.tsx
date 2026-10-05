import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { apiFetch, getToken, newIdempotencyKey } from '../api/client'
import type { BranchDto } from '../api/client'
import * as signalR from '@microsoft/signalr'
import { buildPcDisplay, classifyFloorPc, isOfflineFreePc, resolveOccupancy } from '../occupancy'
import { buildZoneSlots, isAmenityZone, zoneKindLabel } from '../floorGrid'
import {
  FloorMapCanvas,
  defaultElementColor,
  elementKindLabel,
  type FloorMapElement,
  type FloorMapElementKind,
} from '../components/FloorMapCanvas'
import { FloorPcTileFace, tileBottomText } from '../components/FloorPcTile'
import { ActionDialog, ConfirmDialog } from '../components/ActionDialog'
import {
  FloorOrdersDialog,
  FloorQuickBarDialog,
  FloorStartBarCart,
  cartTotal,
  sellBarItemsNow,
  type FloorCartItem,
  type FloorBarOrderDto,
} from '../components/FloorBarExtras'
import { FloorQuickCaseKeyDialog } from '../components/FloorQuickCaseKeyDialog'
import { parseStaffFloorSearch } from '../staffNav'
import { can, Perm } from '../permissions'
import { FloorCustomerPick, type FloorCustomerLite } from '../components/FloorCustomerPick'
import {
  FloorPaymentPick,
  buildMixedPayments,
  dividePaymentParts,
  resolveUnitPaymentFromParts,
  type FloorPayMethod,
} from '../components/FloorPaymentPick'
import { FloorUpcomingFreePanel } from '../components/FloorUpcomingFreePanel'
import { payKaspiTerminalIfReady } from '../kaspiPos'
import {
  bookingStatusLabel,
  isBookingStatus,
  isOpenBookingStatus,
  isSessionStatus,
} from '../format'

/** Экстренный сигнал «ВОЕНКОМАТ»: 1-я строка — крупный заголовок, 2-я — короткий текст. */
const VOENKOMAT_TEXT = 'ВОЕНКОМАТ\nСрочно зайдите к администратору'

type ComputerStatus =
  | 'Offline'
  | 'Online'
  | 'Free'
  | 'Locked'
  | 'InSession'
  | 'Reserved'
  | 'Starting'
  | 'Ending'
  | 'Maintenance'
  | 'Updating'
  | 'Error'
  | 'PendingApproval'

type ComputerDto = {
  id: string
  branchId: string
  zoneId?: string
  zoneName?: string
  zoneColorHex?: string
  displayName?: string
  windowsName: string
  ipAddress?: string
  macAddress?: string
  mapX?: number
  mapY?: number
  status: ComputerStatus | number
  lastSeenAt?: string
  lastHeartbeatAt?: string
  isApproved: boolean
  registrationCode?: string
  clientVersion?: string
  isMaintenance: boolean
  currentSessionId?: string
  remainingSeconds?: number
  sessionTotalPrice?: number
  sessionGuestName?: string
  sessionStatus?: string | number
  sessionCustomerId?: string | null
  occupancy?: string
  occupancyDetail?: string
  gridCol?: number
  gridRow?: number
  gridColSpan?: number
  gridRowSpan?: number
  zoneKind?: string
  zoneGridColumns?: number
  zoneGridRows?: number
  /** Pc | Console — PS5 и т.п. без Shell */
  stationKind?: string | number
}

type TariffDto = {
  id: string
  branchId: string
  zoneId?: string
  name: string
  code: string
  /** API пишет enum числом (FlexibleJsonEnumConverter); строки — совместимость. */
  kind?: string | number
  billingMode?: string | number
  durationMode?: string | number
  pricePerHour: number
  fixedDurationMinutes?: number
  fixedPrice?: number
  minCharge: number
  minDurationMinutes?: number
  maxDurationMinutes?: number
  availableFrom?: string
  availableTo?: string
  isActive: boolean
  isAvailableNow?: boolean
  allowPause?: boolean
  sortOrder?: number
  windowEndsAtLocal?: string
  remainingMinutesInWindow?: number
  salePreview?: string
  /** Активная маркетинговая скидка %, 0 если нет (каталожные цены без скидки). */
  promoPercent?: number
  promoLabel?: string
}

type TariffKindName = 'Hourly' | 'Package' | 'Postpay' | 'Free'

type StartCustomer = FloorCustomerLite

/** Как MarketingPromoMath.Apply на сервере. */
function applyPercentDiscount(amount: number, percent: number) {
  if (amount <= 0 || percent <= 0) return amount
  const p = Math.min(90, Math.max(0, percent))
  return Math.round(amount * (1 - p / 100) * 100) / 100
}

/** Маркетинг, затем лояльность — как StartGuestSessionAsync. */
function applySessionDiscounts(listPrice: number, promoPercent = 0, loyaltyPercent = 0) {
  return applyPercentDiscount(applyPercentDiscount(listPrice, promoPercent), loyaltyPercent)
}

function isTimeWindowTariff(t: Pick<TariffDto, 'durationMode'>) {
  return t.durationMode === 'TimeWindow' || t.durationMode === 1
}

function resolveTariffKind(t: Pick<TariffDto, 'kind' | 'fixedPrice' | 'fixedDurationMinutes' | 'durationMode'>): TariffKindName | null {
  const k = t.kind
  if (k === 'Hourly' || k === 0) return 'Hourly'
  if (k === 'Package' || k === 1) return 'Package'
  if (k === 'Postpay' || k === 2) return 'Postpay'
  if (k === 'Free' || k === 3) return 'Free'
  if (isTimeWindowTariff(t) && t.fixedPrice != null) return 'Package'
  if (t.fixedPrice != null && t.fixedDurationMinutes != null && t.fixedDurationMinutes > 0) return 'Package'
  return null
}

function isPackageTariff(t: Pick<TariffDto, 'kind' | 'fixedPrice' | 'fixedDurationMinutes' | 'durationMode'>) {
  return resolveTariffKind(t) === 'Package'
}

/** Как отправляем extend на API: другой тариф / день-ночь — с tariffId, иначе только минуты. */
function resolveExtendApiPayload(
  picked: TariffDto | null,
  sessionTariffId: string | null | undefined,
  billMinutes: number,
): { additionalMinutes: number; tariffId: string | null } {
  if (!picked || billMinutes <= 0) {
    return { additionalMinutes: Math.max(0, billMinutes), tariffId: null }
  }
  const sessionTid = sessionTariffId ?? null
  if (sessionTid && picked.id !== sessionTid) {
    return { additionalMinutes: billMinutes, tariffId: picked.id }
  }
  if (isTimeWindowTariff(picked)) {
    return { additionalMinutes: billMinutes, tariffId: picked.id }
  }
  return { additionalMinutes: billMinutes, tariffId: null }
}

/** API требует amountTendered для Cash/Kaspi/Mixed; для баланса — null. */
function resolveFloorAmountTendered(
  pay: FloorPayMethod | null,
  amountDue: number,
  cashTenderedRaw: string,
): number | null {
  if (!pay || amountDue <= 0 || pay === 'Balance') return null
  if (pay === 'Cash') {
    const n = cashTenderedRaw ? Number(cashTenderedRaw) : NaN
    return Number.isFinite(n) ? n : null
  }
  // Kaspi / Mixed — ровно сумма к оплате (части в payments)
  return amountDue
}

function resolveBillingMode(t: Pick<TariffDto, 'billingMode'>): 'PerMinute' | 'Block15' | 'Block30' {
  const b = t.billingMode
  if (b === 'Block15' || b === 1) return 'Block15'
  if (b === 'Block30' || b === 2) return 'Block30'
  return 'PerMinute'
}

/** Как TariffAvailability.ResolveDurationMinutes (+ частичное окно дня/ночи). */
function resolveEffectiveStartMinutes(
  t: TariffDto | null | undefined,
  requestedMinutes: number,
) {
  const requested = Math.max(0, Math.floor(Number(requestedMinutes) || 0))
  if (!t || requested <= 0) return 0

  if (isTimeWindowTariff(t)) {
    const rem = t.remainingMinutesInWindow ?? 0
    if (rem > 0) return Math.min(requested, rem)
    return requested
  }

  if (isPackageTariff(t) && t.fixedDurationMinutes && t.fixedDurationMinutes > 0) {
    return t.fixedDurationMinutes
  }

  let m = requested
  if (t.minDurationMinutes && m < t.minDurationMinutes) m = t.minDurationMinutes
  if (t.maxDurationMinutes && m > t.maxDurationMinutes) m = t.maxDurationMinutes
  return m
}

function startDurationPresets(t: TariffDto): number[] {
  if (isTimeWindowTariff(t)) {
    const rem = t.remainingMinutesInWindow ?? 0
    const base = [15, 30, 60, 120].filter((m) => rem <= 0 || m < rem)
    if (rem > 0 && !base.includes(rem)) base.push(rem)
    return base
  }
  if (isPackageTariff(t) && t.fixedDurationMinutes && t.fixedDurationMinutes > 0) {
    return [t.fixedDurationMinutes]
  }
  const min = t.minDurationMinutes && t.minDurationMinutes > 0 ? t.minDurationMinutes : 15
  return [15, 30, 60, 120].filter((m) => m >= min)
}

/** Подпись длительности: ≥60 мин — в часах. */
function formatDurationChip(minutes: number) {
  if (minutes <= 0) return '—'
  if (minutes < 60) return `${minutes} мин`
  if (minutes % 60 === 0) return `${minutes / 60} ч`
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return `${h} ч ${m} мин`
}

type FloorBooking = {
  id: string
  number: string
  customerId?: string | null
  customerName?: string | null
  contactName: string
  contactPhone: string
  status: string | number
  startsAt: string
  endsAt: string
  durationMinutes: number
  prepaidAmount: number
  computers: { computerId: string; computerName?: string; gamingSessionId?: string }[]
}

function todayLocalInput() {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function tomorrowLocalInput() {
  const d = new Date()
  d.setDate(d.getDate() + 1)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function bookingTimeLabel(startsAt: string) {
  return new Date(startsAt).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}

function bookingRangeLabel(b: { startsAt: string; endsAt: string }) {
  return `${bookingTimeLabel(b.startsAt)}–${bookingTimeLabel(b.endsAt)}`
}

const SOFT_HOLD_MS = 30 * 60_000

type BookingBlockReason = 'soft-hold' | 'overlap' | 'arrived'

function explainBookingBlock(
  b: FloorBooking,
  durationMin: number,
): { blocked: boolean; reason?: BookingBlockReason; message?: string } {
  if (!isOpenBookingStatus(b.status)) {
    return { blocked: false }
  }
  const now = Date.now()
  const starts = new Date(b.startsAt).getTime()
  const ends = new Date(b.endsAt).getTime()
  if (ends <= now) return { blocked: false }

  if (isBookingStatus(b.status, 'Arrived', 'Active')) {
    return {
      blocked: true,
      reason: 'arrived',
      message: `Клиент по брони ${b.number} уже ожидается (${bookingRangeLabel(b)}). Запустите «По брони» или отмените бронь (Active тоже можно отменить — сеансы закроются).`,
    }
  }

  if (starts <= now + SOFT_HOLD_MS) {
    return {
      blocked: true,
      reason: 'soft-hold',
      message: `До брони ${b.number} меньше 30 мин (${bookingTimeLabel(b.startsAt)}). ПК зарезервирован — старт только по брони или отмена.`,
    }
  }

  const sessionEnd = now + Math.max(1, durationMin) * 60_000
  if (starts < sessionEnd) {
    const maxMin = Math.max(0, Math.floor((starts - now) / 60_000))
    return {
      blocked: true,
      reason: 'overlap',
      message: `Сеанс на ${formatDurationChip(durationMin)} не успеет: бронь ${b.number} с ${bookingTimeLabel(b.startsAt)}. Максимум сейчас ~${formatDurationChip(maxMin)}.`,
    }
  }

  return { blocked: false }
}

function guestStatusLine(pc: {
  status: ComputerDto['status']
  occupancy?: string
  occupancyDetail?: string
  isApproved: boolean
  currentSessionId?: string
  sessionStatus?: string | number
  remainingSeconds?: number
}) {
  const view = buildPcDisplay(pc)
  return view.statusLabel
}

function pcIsOfflineFree(pc: ComputerDto) {
  return buildPcDisplay(pc).offlineBadge
}

function pcIsConsole(pc: ComputerDto) {
  const k = pc.stationKind
  return k === 'Console' || k === 1
}

function pcCanWake(pc: ComputerDto) {
  if (pcIsConsole(pc)) return false
  return !!pc.macAddress?.trim()
}

type FloorViewMode = 'grid' | 'map'

function readFloorView(): FloorViewMode {
  try {
    const v = localStorage.getItem('shift.floorView')
    if (v === 'map' || v === 'grid') return v
  } catch {
    /* ignore */
  }
  return 'map'
}

type FloorMapDto = {
  branchId: string
  gridCols: number
  gridRows: number
  backgroundHex?: string | null
  computers: ComputerDto[]
  elements: FloorMapElement[]
}

function elementUpsertBody(
  el: FloorMapElement,
  patch: Partial<{
    label: string | null
    gridCol: number
    gridRow: number
    colSpan: number
    rowSpan: number
    endCol: number | null
    endRow: number | null
    colorHex: string | null
    fillHex: string | null
    strokeWidth: number | null
    fontSize: number | null
    sortOrder: number
    showIcon: boolean
    rotationDeg: number
    isVisible: boolean
  }> = {},
) {
  return {
    kind: el.kind,
    label: patch.label !== undefined ? patch.label : el.label,
    gridCol: patch.gridCol ?? el.gridCol,
    gridRow: patch.gridRow ?? el.gridRow,
    colSpan: patch.colSpan ?? el.colSpan,
    rowSpan: patch.rowSpan ?? el.rowSpan,
    endCol: patch.endCol !== undefined ? patch.endCol : el.endCol,
    endRow: patch.endRow !== undefined ? patch.endRow : el.endRow,
    colorHex: patch.colorHex !== undefined ? patch.colorHex : el.colorHex,
    fillHex: patch.fillHex !== undefined ? patch.fillHex : el.fillHex,
    strokeWidth: patch.strokeWidth !== undefined ? patch.strokeWidth : el.strokeWidth,
    fontSize: patch.fontSize !== undefined ? patch.fontSize : el.fontSize,
    sortOrder: patch.sortOrder ?? el.sortOrder ?? 0,
    showIcon: patch.showIcon ?? el.showIcon ?? true,
    rotationDeg: patch.rotationDeg ?? el.rotationDeg ?? 0,
    isVisible: patch.isVisible ?? el.isVisible,
  }
}

type LayoutTool = 'select' | 'Cashier' | 'Entrance' | 'Restroom' | 'Bar' | 'Label' | 'Rect' | 'Wall'

const LAYOUT_PALETTE: { kind: LayoutTool; label: string }[] = [
  { kind: 'select', label: 'Выбор' },
  { kind: 'Cashier', label: 'Касса' },
  { kind: 'Entrance', label: 'Вход' },
  { kind: 'Restroom', label: 'Уборная' },
  { kind: 'Bar', label: 'Бар' },
  { kind: 'Label', label: 'Подпись' },
  { kind: 'Rect', label: 'Зона' },
  { kind: 'Wall', label: 'Стена' },
]

export function FloorMapPage() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const location = useLocation()
  const canManagePcs = can(Perm.ComputersManage)
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const selectedId = selectedIds[selectedIds.length - 1] ?? null
  const [zoneId, setZoneId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [tariffId, setTariffId] = useState('')
  const [durationMinutes, setDurationMinutes] = useState(60)
  const [startPaidAmount, setStartPaidAmount] = useState('')
  const [startCashTendered, setStartCashTendered] = useState('')
  const [paymentMethod, setPaymentMethod] = useState<FloorPayMethod | null>(null)
  const [startMixedCash, setStartMixedCash] = useState('')
  const [startCustomerId, setStartCustomerId] = useState('')
  const [startCustomerQuery, setStartCustomerQuery] = useState('')
  const [startCustomerPicked, setStartCustomerPicked] = useState<StartCustomer | null>(null)
  const [useTimeBank, setUseTimeBank] = useState(false)
  const [wakeBeforeStart, setWakeBeforeStart] = useState(true)
  const [sessionError, setSessionError] = useState<string | null>(null)
  const [startOpen, setStartOpen] = useState(false)
  const [bookOpen, setBookOpen] = useState(false)
  const [approveOpen, setApproveOpen] = useState(false)
  const [endConfirm, setEndConfirm] = useState<'end' | 'save' | null>(null)
  const [attachOpen, setAttachOpen] = useState(false)
  const [attachThenSave, setAttachThenSave] = useState(false)
  const [attachQuery, setAttachQuery] = useState('')
  const [attachPicked, setAttachPicked] = useState<FloorCustomerLite | null>(null)
  const [techActionConfirm, setTechActionConfirm] = useState<{
    type: string
    label: string
    payloadJson?: string | null
  } | null>(null)
  const [techOpen, setTechOpen] = useState(false)
  const [voenkomatOpen, setVoenkomatOpen] = useState(false)
  const [techMessage, setTechMessage] = useState('')
  const [techCmdLine, setTechCmdLine] = useState('')
  const [techCmdResult, setTechCmdResult] = useState<string | null>(null)
  const [techCmdRunning, setTechCmdRunning] = useState(false)
  const [messageOpen, setMessageOpen] = useState(false)
  const [taskMgrOpen, setTaskMgrOpen] = useState(false)
  const [taskMgrLoading, setTaskMgrLoading] = useState(false)
  const [taskMgrError, setTaskMgrError] = useState<string | null>(null)
  const [taskMgrFilter, setTaskMgrFilter] = useState('')
  const [taskMgrProcesses, setTaskMgrProcesses] = useState<
    { pid: number; name: string; memoryMb: number; windowTitle?: string; canKill: boolean }[]
  >([])
  const [floorView, setFloorView] = useState<FloorViewMode>(readFloorView)
  const [taskMgrCapturedAt, setTaskMgrCapturedAt] = useState<string | null>(null)
  const [transferOpen, setTransferOpen] = useState(false)
  const [transferTargetId, setTransferTargetId] = useState('')
  const [extendOpen, setExtendOpen] = useState(false)
  const [extendMinutes, setExtendMinutes] = useState(30)
  const [extendTariffId, setExtendTariffId] = useState<string | null>(null)
  const [extendPaidAmount, setExtendPaidAmount] = useState('')
  const [extendPay, setExtendPay] = useState<FloorPayMethod | null>(null)
  const [extendTendered, setExtendTendered] = useState('')
  const [extendMixedCash, setExtendMixedCash] = useState('')
  const [startFromBookingId, setStartFromBookingId] = useState<string | null>(null)
  const [bookingConflict, setBookingConflict] = useState<FloorBooking | null>(null)
  const [cancelBookingId, setCancelBookingId] = useState<string | null>(null)
  const [startBarCart, setStartBarCart] = useState<FloorCartItem[]>([])
  const [ordersOpen, setOrdersOpen] = useState(false)
  const [ordersHighlightId, setOrdersHighlightId] = useState<string | null>(null)
  const [ordersFilterPcId, setOrdersFilterPcId] = useState<string | null>(null)
  const [quickBarOpen, setQuickBarOpen] = useState(false)
  const [quickCaseKeyOpen, setQuickCaseKeyOpen] = useState(false)
  const [floorResult, setFloorResult] = useState<{
    title: string
    message: string
    tone: 'success' | 'warning' | 'error'
  } | null>(null)
  const [pendingPcsOpen, setPendingPcsOpen] = useState(false)
  const [bookContactName, setBookContactName] = useState('')
  const [bookContactPhone, setBookContactPhone] = useState('')
  const [bookDate, setBookDate] = useState(todayLocalInput())
  const [bookTime, setBookTime] = useState('18:00')
  const [bookDuration, setBookDuration] = useState(60)
  const [bookPrepaid, setBookPrepaid] = useState(0)
  const [bookPrepayMethod, setBookPrepayMethod] = useState<'Cash' | 'KaspiQr'>('Cash')
  const [bookComment, setBookComment] = useState('')
  const skipClickRef = useRef(false)
  const [layoutEdit, setLayoutEdit] = useState(false)
  const [layoutTool, setLayoutTool] = useState<LayoutTool>('select')
  const [wallDraft, setWallDraft] = useState<{ col: number; row: number } | null>(null)
  const [selectedElementId, setSelectedElementId] = useState<string | null>(null)
  const [elementLabelDraft, setElementLabelDraft] = useState('')
  const [gridColsDraft, setGridColsDraft] = useState(24)
  const [gridRowsDraft, setGridRowsDraft] = useState(16)
  const [bgDraft, setBgDraft] = useState('#0f172a')

  const computersQuery = useQuery({
    queryKey: ['computers'],
    queryFn: async () => (await apiFetch<ComputerDto[]>('/api/computers')).data ?? [],
    refetchInterval: 8000,
  })

  const floorMapQuery = useQuery({
    queryKey: ['floor-map'],
    queryFn: async () => (await apiFetch<FloorMapDto>('/api/floor-map')).data!,
    refetchInterval: 8000,
  })

  useEffect(() => {
    const map = floorMapQuery.data
    if (!map) return
    setGridColsDraft(map.gridCols)
    setGridRowsDraft(map.gridRows)
    setBgDraft(map.backgroundHex ?? '#0f172a')
  }, [floorMapQuery.data?.gridCols, floorMapQuery.data?.gridRows, floorMapQuery.data?.backgroundHex])

  const branchesQuery = useQuery({
    queryKey: ['branches'],
    queryFn: async () => (await apiFetch<BranchDto[]>('/api/branches')).data ?? [],
  })

  const tariffsQuery = useQuery({
    queryKey: ['tariffs', 'floor'],
    queryFn: async () => (await apiFetch<TariffDto[]>('/api/tariffs')).data ?? [],
  })

  const bookingsQuery = useQuery({
    queryKey: ['bookings', 'floor', todayLocalInput(), tomorrowLocalInput()],
    queryFn: async () => {
      const today = todayLocalInput()
      const tomorrow = tomorrowLocalInput()
      const [a, b] = await Promise.all([
        apiFetch<FloorBooking[]>(`/api/bookings?date=${today}`),
        apiFetch<FloorBooking[]>(`/api/bookings?date=${tomorrow}`),
      ])
      const map = new Map<string, FloorBooking>()
      for (const item of [...(a.data ?? []), ...(b.data ?? [])]) {
        map.set(item.id, item)
      }
      return [...map.values()]
    },
    refetchInterval: 20000,
  })

  const barOrdersQuery = useQuery({
    queryKey: ['bar-orders'],
    queryFn: async () => (await apiFetch<FloorBarOrderDto[]>('/api/bar/orders')).data ?? [],
    refetchInterval: 8000,
    enabled: can(Perm.BarOrders),
  })
  const openBarOrderCount = barOrdersQuery.data?.length ?? 0
  const pendingPcs = useMemo(
    () => (computersQuery.data ?? []).filter((c) => !c.isApproved),
    [computersQuery.data],
  )
  const pendingPcCount = pendingPcs.length

  const startCustomersQuery = useQuery({
    queryKey: ['customers', 'start', startCustomerQuery],
    queryFn: async () =>
      (await apiFetch<StartCustomer[]>(`/api/customers?q=${encodeURIComponent(startCustomerQuery)}`)).data ??
      [],
    enabled: startOpen && startCustomerQuery.trim().length >= 2,
  })

  const attachCustomersQuery = useQuery({
    queryKey: ['customers', 'attach', attachQuery],
    queryFn: async () =>
      (await apiFetch<FloorCustomerLite[]>(`/api/customers?q=${encodeURIComponent(attachQuery)}`)).data ?? [],
    enabled: attachOpen && attachQuery.trim().length >= 2,
  })

  const bookingsByPc = useMemo(() => {
    const map = new Map<string, FloorBooking>()
    const now = Date.now()
    for (const b of bookingsQuery.data ?? []) {
      if (!isOpenBookingStatus(b.status)) continue
      // Прошедшие слоты не показываем
      if (new Date(b.endsAt).getTime() <= now) continue
      for (const c of b.computers) {
        const prev = map.get(c.computerId)
        if (!prev) {
          map.set(c.computerId, b)
          continue
        }
        // Ближайшая по времени начала (текущая/будущая)
        if (new Date(b.startsAt).getTime() < new Date(prev.startsAt).getTime()) {
          map.set(c.computerId, b)
        }
      }
    }
    return map
  }, [bookingsQuery.data])

  const selectedBooking = selectedId ? bookingsByPc.get(selectedId) ?? null : null

  function showFloorResult(
    title: string,
    message: string,
    tone: 'success' | 'warning' | 'error' = 'success',
  ) {
    setSessionError(null)
    setFloorResult({ title, message, tone })
  }

  type SessionLite = {
    id: string
    status: string | number
    remainingSeconds?: number
    allowPause: boolean
    guestName?: string
    totalPrice: number
    customerId?: string
    tariffId?: string
    tariffName?: string
    zoneId?: string
    zoneName?: string
    paymentMethod?: string
  }

  const zones = branchesQuery.data?.[0]?.zones ?? []

  function selectComputer(id: string, additive: boolean) {
    setSessionError(null)
    setSelectedIds((prev) => {
      if (additive) {
        return prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]
      }
      return prev.length === 1 && prev[0] === id ? prev : [id]
    })
  }

  useEffect(() => {
    const focus = parseStaffFloorSearch(location.search)
    if (!focus.focus && !focus.computerId && !focus.orderId) return

    if (focus.computerId) selectComputer(focus.computerId, false)
    if (focus.focus === 'bar' || focus.orderId) {
      setOrdersHighlightId(focus.orderId ?? null)
      setOrdersFilterPcId(focus.computerId ?? null)
      setOrdersOpen(true)
    }
    navigate('/floor', { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.search])

  const selected = useMemo(
    () => computersQuery.data?.find((c) => c.id === selectedId) ?? null,
    [computersQuery.data, selectedId],
  )

  const selectedComputers = useMemo(
    () => (computersQuery.data ?? []).filter((c) => selectedIds.includes(c.id)),
    [computersQuery.data, selectedIds],
  )

  /** Подтверждённые ПК для панели «Техника» (консоли без агента — исключаем). */
  const techTargets = useMemo(
    () => selectedComputers.filter((c) => c.isApproved && !pcIsConsole(c)),
    [selectedComputers],
  )

  const freeStartableSelected = useMemo(
    () =>
      selectedComputers.filter(
        (c) => c.isApproved && !c.currentSessionId && !c.isMaintenance && resolveOccupancy(c).kind === 'Free',
      ),
    [selectedComputers],
  )

  /** ПК с активным сеансом — продление сразу на всех выбранных. */
  const extendTargets = useMemo(
    () =>
      selectedComputers.filter(
        (c) => !!c.currentSessionId && !isSessionStatus(c.sessionStatus, 'Paused'),
      ),
    [selectedComputers],
  )

  /** ПК, на которых можно открыть диалог старта (Free или Reserved под бронь). */
  const startDialogTargets = useMemo(() => {
    if (startFromBookingId) {
      return selectedComputers.filter((c) => c.isApproved && !c.currentSessionId && !c.isMaintenance)
    }
    const reservedOk = selectedComputers.filter(
      (c) =>
        c.isApproved &&
        !c.currentSessionId &&
        !c.isMaintenance &&
        (resolveOccupancy(c).kind === 'Free' || resolveOccupancy(c).kind === 'Reserved'),
    )
    return freeStartableSelected.length > 0 ? freeStartableSelected : reservedOk
  }, [selectedComputers, freeStartableSelected, startFromBookingId])

  const startCustomer = startCustomerPicked
  const startPcZoneId = selected?.zoneId ?? startDialogTargets[0]?.zoneId
  const startCustomerBank =
    startCustomer?.timeBanks?.find((b) => b.zoneId === startPcZoneId)?.minutes ?? 0
  const canUseTimeBank =
    !!startCustomerId && startCustomerBank > 0 && startDialogTargets.length <= 1 && !startFromBookingId

  const startOfflineTargets = useMemo(
    () => startDialogTargets.filter((c) => pcIsOfflineFree(c)),
    [startDialogTargets],
  )
  const startWakeTargets = useMemo(
    () => startOfflineTargets.filter((c) => pcCanWake(c)),
    [startOfflineTargets],
  )
  const startOfflineNoMac = useMemo(
    () => startOfflineTargets.filter((c) => !pcCanWake(c)),
    [startOfflineTargets],
  )

  useEffect(() => {
    if (!startOpen) return
    setWakeBeforeStart(startWakeTargets.length > 0)
  }, [startOpen, startWakeTargets.length])

  const freeComputers = useMemo(
    () =>
      (computersQuery.data ?? []).filter(
        (c) =>
          c.isApproved &&
          c.id !== selected?.id &&
          !c.currentSessionId &&
          !c.isMaintenance &&
          (resolveOccupancy(c).kind === 'Free' || resolveOccupancy(c).kind === 'Offline') &&
          // Same zone only — Standard→VIP transfer is not allowed.
          (!!selected?.zoneId ? c.zoneId === selected.zoneId : !c.zoneId),
      ),
    [computersQuery.data, selected?.id, selected?.zoneId],
  )

  const approvedFloorPcs = useMemo(
    () =>
      (computersQuery.data ?? [])
        .filter((c) => c.isApproved)
        .sort((a, b) =>
          (a.displayName ?? a.windowsName).localeCompare(b.displayName ?? b.windowsName, 'ru', {
            numeric: true,
          }),
        ),
    [computersQuery.data],
  )

  const floorZones = useMemo(() => {
    const zoneOrder = new Map(zones.map((z, i) => [z.id, z.sortOrder ?? i]))
    const groups = new Map<
      string,
      {
        id: string
        name: string
        color?: string
        kind: string
        gridColumns: number
        gridRows: number
        pcs: ComputerDto[]
      }
    >()

    for (const z of zones) {
      if (z.isActive === false) continue
      groups.set(z.id, {
        id: z.id,
        name: z.name,
        color: z.colorHex,
        kind: z.kind ?? 'Hall',
        gridColumns: z.gridColumns ?? 6,
        gridRows: z.gridRows ?? 4,
        pcs: [],
      })
    }

    for (const pc of approvedFloorPcs) {
      const id = pc.zoneId ?? '__none__'
      const known = zones.find((z) => z.id === pc.zoneId)
      if (!groups.has(id)) {
        groups.set(id, {
          id,
          name: pc.zoneName ?? known?.name ?? 'Без зоны',
          color: pc.zoneColorHex ?? known?.colorHex,
          kind: pc.zoneKind ?? known?.kind ?? 'Hall',
          gridColumns: pc.zoneGridColumns ?? known?.gridColumns ?? 6,
          gridRows: pc.zoneGridRows ?? known?.gridRows ?? 4,
          pcs: [],
        })
      }
      groups.get(id)!.pcs.push(pc)
    }

    return [...groups.values()].sort((a, b) => {
      const ai = a.id === '__none__' ? 9999 : (zoneOrder.get(a.id) ?? 500)
      const bi = b.id === '__none__' ? 9999 : (zoneOrder.get(b.id) ?? 500)
      return ai - bi || a.name.localeCompare(b.name, 'ru')
    })
  }, [approvedFloorPcs, zones])

  const floorCounts = useMemo(() => {
    let free = 0
    let busy = 0
    let reserved = 0
    let offline = 0
    for (const pc of approvedFloorPcs) {
      const booking = bookingsByPc.get(pc.id)
      const bucket = classifyFloorPc(
        pc,
        booking ? { startsAt: booking.startsAt, endsAt: booking.endsAt } : null,
      )
      if (bucket === 'onlineFree') free++
      else if (bucket === 'offlineFree') offline++
      else if (bucket === 'busy') busy++
      else if (bucket === 'reserved') reserved++
    }
    return { free, busy, reserved, offline, total: approvedFloorPcs.length }
  }, [approvedFloorPcs, bookingsByPc])

  function setFloorViewMode(mode: FloorViewMode) {
    setFloorView(mode)
    try {
      localStorage.setItem('shift.floorView', mode)
    } catch {
      /* ignore */
    }
  }

  function pcTileView(computer: ComputerDto) {
    const booking = bookingsByPc.get(computer.id)
    return buildPcDisplay(
      computer,
      booking ? { startsAt: booking.startsAt, endsAt: booking.endsAt } : null,
    )
  }

  const activeSessionQuery = useQuery({
    queryKey: ['session-by-pc', selected?.id, selected?.currentSessionId],
    queryFn: async () => {
      if (!selected?.id) return null
      try {
        return (await apiFetch<SessionLite>(`/api/sessions/by-computer/${selected.id}`)).data ?? null
      } catch {
        return null
      }
    },
    enabled: !!selected?.currentSessionId,
    refetchInterval: 5000,
  })

  const availableTariffs = useMemo(() => {
    const all = (tariffsQuery.data ?? []).filter((t) => t.isAvailableNow !== false)
    if (selectedComputers.length > 1) {
      const zoneIds = [...new Set(selectedComputers.map((c) => c.zoneId).filter(Boolean))] as string[]
      if (zoneIds.length === 0) return all.filter((t) => !t.zoneId)
      // Только тарифы, подходящие ВСЕМ выбранным ПК (пересечение зон).
      return all.filter((t) => !t.zoneId || zoneIds.every((z) => t.zoneId === z))
    }
    if (selected?.zoneId) return all.filter((t) => !t.zoneId || t.zoneId === selected.zoneId)
    return all
  }, [tariffsQuery.data, selected?.zoneId, selectedComputers])

  useEffect(() => {
    const t = availableTariffs.find((x) => x.id === tariffId)
    if (!t) return
    setStartPaidAmount('')
    if (isTimeWindowTariff(t) && t.remainingMinutesInWindow && t.remainingMinutesInWindow > 0) {
      setDurationMinutes(t.remainingMinutesInWindow)
    } else if (isPackageTariff(t) && t.fixedDurationMinutes) {
      setDurationMinutes(t.fixedDurationMinutes)
    } else if (resolveTariffKind(t) === 'Hourly') {
      setDurationMinutes(t.minDurationMinutes && t.minDurationMinutes > 0 ? t.minDurationMinutes : 60)
    }
  }, [tariffId])

  useEffect(() => {
    if (availableTariffs.length && !availableTariffs.some((t) => t.id === tariffId)) {
      setTariffId(availableTariffs[0].id)
    }
  }, [availableTariffs, tariffId])

  async function refreshFloorLive(opts?: { clearSessionPcId?: string | null }) {
    if (opts?.clearSessionPcId) {
      queryClient.setQueryData<ComputerDto[]>(['computers'], (prev) =>
        (prev ?? []).map((c) =>
          c.id === opts.clearSessionPcId
            ? {
                ...c,
                currentSessionId: undefined,
                remainingSeconds: undefined,
                sessionStatus: undefined,
                occupancy: 'Free',
                occupancyDetail: undefined,
              }
            : c,
        ),
      )
    }
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['computers'] }),
      queryClient.invalidateQueries({ queryKey: ['session-by-pc'] }),
      queryClient.invalidateQueries({ queryKey: ['bookings'] }),
    ])
    await queryClient.refetchQueries({ queryKey: ['computers'] })
  }

  useEffect(() => {
    const token = getToken()
    if (!token) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/staff?access_token=${encodeURIComponent(token)}`)
      .withAutomaticReconnect()
      .build()

    let debounceTimer: ReturnType<typeof setTimeout> | null = null
    const scheduleFloorRefresh = () => {
      if (debounceTimer) clearTimeout(debounceTimer)
      debounceTimer = setTimeout(() => {
        debounceTimer = null
        void refreshFloorLive()
        void queryClient.invalidateQueries({ queryKey: ['floor-map'] })
      }, 1200)
    }

    connection.on('ComputerStatusChanged', scheduleFloorRefresh)
    connection.on('ComputerApproved', scheduleFloorRefresh)
    connection.on('SessionStarted', scheduleFloorRefresh)
    connection.on('SessionUpdated', scheduleFloorRefresh)
    connection.on('SessionEnded', scheduleFloorRefresh)
    connection.on('SessionWarning', scheduleFloorRefresh)
    connection.on('BookingChanged', () => {
      if (debounceTimer) clearTimeout(debounceTimer)
      debounceTimer = setTimeout(() => {
        debounceTimer = null
        void queryClient.invalidateQueries({ queryKey: ['bookings'] })
        void queryClient.invalidateQueries({ queryKey: ['computers'] })
      }, 800)
    })

    void connection.start()
    return () => {
      if (debounceTimer) clearTimeout(debounceTimer)
      void connection.stop()
    }
  }, [queryClient])

  const bookingActionMutation = useMutation({
    mutationFn: async (payload: { id: string; action: 'arrived' | 'confirm' }) =>
      apiFetch(`/api/bookings/${payload.id}/${payload.action}`, { method: 'POST' }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['bookings'] })
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
      showFloorResult('Гость отмечен', 'Статус брони обновлён: гость пришёл.')
    },
    onError: (err) =>
      showFloorResult(
        'Не удалось обновить бронь',
        err instanceof Error ? err.message : 'Ошибка брони',
        'error',
      ),
  })

  const approveMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId || !zoneId || !displayName.trim()) throw new Error('Заполните имя и зону')
      return apiFetch(`/api/computers/${selectedId}/approve`, {
        method: 'POST',
        body: JSON.stringify({ displayName, zoneId, mapX: 80, mapY: 80 }),
      })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
      setApproveOpen(false)
      setSelectedIds([])
    },
  })

  const commandMutation = useMutation({
    mutationFn: async (opts: {
      type: string
      payloadJson?: string | null
      computerIds?: string[]
    }) => {
      const ids = opts.computerIds?.length
        ? opts.computerIds
        : techTargets.map((c) => c.id)
      if (ids.length === 0) throw new Error('Выберите ПК')

      const results = await Promise.allSettled(
        ids.map((id) =>
          apiFetch(`/api/computers/${id}/commands`, {
            method: 'POST',
            body: JSON.stringify({
              type: opts.type,
              payloadJson: opts.payloadJson ?? null,
              idempotencyKey: newIdempotencyKey(),
            }),
          }),
        ),
      )
      const failed = results.filter((r) => r.status === 'rejected')
      if (failed.length === results.length) {
        const first = failed[0]
        const msg =
          first?.status === 'rejected' && first.reason instanceof Error
            ? first.reason.message
            : 'Ошибка команды'
        throw new Error(msg)
      }
      return { ok: results.length - failed.length, failed: failed.length, total: results.length }
    },
    onSuccess: (res) => {
      setMessageOpen(false)
      setTechMessage('')
      if (res.failed > 0) {
        showFloorResult(
          'Команда частично отправлена',
          `Команда отправлена на ${res.ok} из ${res.total} ПК.`,
          'warning',
        )
      } else {
        showFloorResult('Команда отправлена', `Команда отправлена на ${res.total} ПК.`)
      }
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) =>
      showFloorResult(
        'Не удалось отправить команду',
        err instanceof Error ? err.message : 'Ошибка команды',
        'error',
      ),
  })

  const wakeMutation = useMutation({
    mutationFn: async (computerIds?: string[]) => {
      const ids =
        computerIds?.length
          ? computerIds
          : techTargets
              .filter((c) => !!c.macAddress?.trim() && !c.currentSessionId)
              .map((c) => c.id)
      if (ids.length === 0) throw new Error('Нет ПК для включения (нужен MAC, без сеанса)')

      const results = await Promise.allSettled(
        ids.map((id) => apiFetch(`/api/computers/${id}/wake`, { method: 'POST', body: '{}' })),
      )
      const failed = results.filter((r) => r.status === 'rejected')
      if (failed.length === results.length) {
        const first = failed[0]
        const msg =
          first?.status === 'rejected' && first.reason instanceof Error
            ? first.reason.message
            : 'Ошибка включения'
        throw new Error(msg)
      }
      return { ok: results.length - failed.length, failed: failed.length, total: results.length }
    },
    onSuccess: (res) => {
      if (res.failed > 0) {
        showFloorResult(
          'Wake-on-LAN частично',
          `Включение отправлено на ${res.ok} из ${res.total} ПК.`,
          'warning',
        )
      } else {
        showFloorResult('Wake-on-LAN', `Сигнал включения отправлен на ${res.total} ПК.`)
      }
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) =>
      showFloorResult(
        'Не удалось включить ПК',
        err instanceof Error ? err.message : 'Ошибка включения',
        'error',
      ),
  })

  const voenkomatMutation = useMutation({
    mutationFn: async () => {
      const targets = (computersQuery.data ?? []).filter((c) => c.isApproved)
      if (targets.length === 0) throw new Error('Нет подтверждённых ПК')
      const results = await Promise.allSettled(
        targets.map((c) =>
          apiFetch(`/api/computers/${c.id}/commands`, {
            method: 'POST',
            body: JSON.stringify({
              type: 'EmergencyAlert',
              payloadJson: JSON.stringify({ text: VOENKOMAT_TEXT }),
              idempotencyKey: newIdempotencyKey(),
            }),
          }),
        ),
      )
      const failed = results.filter((r) => r.status === 'rejected').length
      return { ok: targets.length - failed, total: targets.length }
    },
    onSuccess: (res) => {
      setVoenkomatOpen(false)
      if (res.ok < res.total) {
        showFloorResult(
          'Сигнал частично отправлен',
          `Сигнал отправлен на ${res.ok} из ${res.total} ПК.`,
          'warning',
        )
      } else {
        showFloorResult('Сигнал отправлен', `Сообщение отправлено на все ПК (${res.total}).`)
      }
    },
    onError: (err) =>
      showFloorResult(
        'Не удалось отправить сигнал',
        err instanceof Error ? err.message : 'Ошибка отправки сигнала',
        'error',
      ),
  })

  type TaskProcess = {
    pid: number
    name: string
    memoryMb: number
    windowTitle?: string
    canKill: boolean
  }

  type CommandDto = {
    id: string
    status: string | number
    resultJson?: string | null
    errorMessage?: string | null
  }

  function isCommandFailed(status: string | number) {
    return status === 'Failed' || status === 5
  }

  function isCommandDone(status: string | number) {
    return status === 'Completed' || status === 'Failed' || status === 4 || status === 5
  }

  async function waitCommandResult(computerId: string, commandId: string, timeoutMs = 20000): Promise<CommandDto> {
    const started = Date.now()
    while (Date.now() - started < timeoutMs) {
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
    throw new Error('ПК не ответил вовремя (нужен Shell 0.7.4+)')
  }

  function formatCmdResult(resultJson?: string | null): string {
    if (!resultJson) return 'Команда выполнена (нет вывода)'
    try {
      const raw = JSON.parse(resultJson) as {
        exitCode?: number
        stdout?: string
        stderr?: string
        command?: string
      }
      const parts: string[] = []
      if (raw.command) parts.push(`> ${raw.command}`)
      if (raw.exitCode != null) parts.push(`код выхода: ${raw.exitCode}`)
      if (raw.stdout?.trim()) parts.push(raw.stdout.trimEnd())
      if (raw.stderr?.trim()) parts.push(`[stderr]\n${raw.stderr.trimEnd()}`)
      return parts.join('\n\n') || 'Команда выполнена (пустой вывод)'
    } catch {
      return resultJson
    }
  }

  async function runRemoteCmdOnTargets(commandLine: string, computerIds: string[]) {
    const line = commandLine.trim()
    if (!line) throw new Error('Введите команду')
    if (computerIds.length === 0) throw new Error('Выберите ПК')
    const payloadJson = JSON.stringify({ command: line })

    if (computerIds.length === 1) {
      setTechCmdRunning(true)
      setTechCmdResult(null)
      setSessionError(null)
      try {
        const sent = await apiFetch<CommandDto>(`/api/computers/${computerIds[0]}/commands`, {
          method: 'POST',
          body: JSON.stringify({
            type: 'RunCmd',
            payloadJson,
            idempotencyKey: newIdempotencyKey(),
          }),
        })
        if (!sent.data?.id) throw new Error('Не удалось отправить команду')
        const done = await waitCommandResult(computerIds[0], sent.data.id, 100_000)
        setTechCmdResult(formatCmdResult(done.resultJson))
        void queryClient.invalidateQueries({ queryKey: ['computers'] })
      } finally {
        setTechCmdRunning(false)
      }
      return
    }

    await commandMutation.mutateAsync({
      type: 'RunCmd',
      payloadJson,
      computerIds,
    })
    setTechCmdResult(
      `Команда отправлена на ${computerIds.length} ПК.\nВывод смотрите по одному ПК (выберите один и выполните снова).`,
    )
  }

  function parseProcessSnapshot(resultJson?: string | null): TaskProcess[] {
    if (!resultJson) return []
    try {
      const raw = JSON.parse(resultJson) as {
        processes?: TaskProcess[]
        Processes?: TaskProcess[]
      }
      const list = raw.processes ?? raw.Processes ?? []
      return list.map((p) => ({
        pid: Number((p as TaskProcess).pid ?? (p as { Pid?: number }).Pid),
        name: String((p as TaskProcess).name ?? (p as { Name?: string }).Name ?? '?'),
        memoryMb: Number((p as TaskProcess).memoryMb ?? (p as { MemoryMb?: number }).MemoryMb ?? 0),
        windowTitle:
          (p as TaskProcess).windowTitle ?? (p as { WindowTitle?: string }).WindowTitle ?? undefined,
        canKill: Boolean((p as TaskProcess).canKill ?? (p as { CanKill?: boolean }).CanKill),
      }))
    } catch {
      return []
    }
  }

  async function refreshTaskManager(openOnPc: boolean) {
    if (!selectedId) return
    setTaskMgrLoading(true)
    setTaskMgrError(null)
    try {
      const sent = await apiFetch<CommandDto>(`/api/computers/${selectedId}/commands`, {
        method: 'POST',
        body: JSON.stringify({
          type: openOnPc ? 'OpenTaskManager' : 'GetDiagnostics',
          payloadJson: null,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
      if (!sent.data?.id) throw new Error('Не удалось отправить команду')
      const done = await waitCommandResult(selectedId, sent.data.id)
      const processes = parseProcessSnapshot(done.resultJson)
      setTaskMgrProcesses(processes)
      setTaskMgrCapturedAt(new Date().toLocaleTimeString('ru-RU'))
      if (processes.length === 0) {
        setTaskMgrError('Список пуст — проверьте, что на ПК Shell 0.6.15+')
      }
    } catch (e) {
      setTaskMgrError(e instanceof Error ? e.message : 'Ошибка диспетчера')
    } finally {
      setTaskMgrLoading(false)
    }
  }

  async function killRemoteProcess(proc: TaskProcess) {
    if (!selectedId || !proc.canKill) return
    if (!window.confirm(`Завершить ${proc.name} (PID ${proc.pid}) на ПК?`)) return
    setTaskMgrLoading(true)
    setTaskMgrError(null)
    try {
      const sent = await apiFetch<CommandDto>(`/api/computers/${selectedId}/commands`, {
        method: 'POST',
        body: JSON.stringify({
          type: 'CloseApplication',
          payloadJson: JSON.stringify({ pid: proc.pid }),
          idempotencyKey: newIdempotencyKey(),
        }),
      })
      if (!sent.data?.id) throw new Error('Не удалось отправить команду')
      const done = await waitCommandResult(selectedId, sent.data.id)
      const processes = parseProcessSnapshot(done.resultJson)
      if (processes.length > 0) {
        setTaskMgrProcesses(processes)
        setTaskMgrCapturedAt(new Date().toLocaleTimeString('ru-RU'))
      } else {
        await refreshTaskManager(false)
      }
    } catch (e) {
      setTaskMgrError(e instanceof Error ? e.message : 'Не удалось завершить')
    } finally {
      setTaskMgrLoading(false)
    }
  }

  const filteredTaskMgr = useMemo(() => {
    const q = taskMgrFilter.trim().toLowerCase()
    if (!q) return taskMgrProcesses
    return taskMgrProcesses.filter(
      (p) =>
        p.name.toLowerCase().includes(q) ||
        String(p.pid).includes(q) ||
        (p.windowTitle?.toLowerCase().includes(q) ?? false),
    )
  }, [taskMgrFilter, taskMgrProcesses])

  const transferMutation = useMutation({
    mutationFn: async () => {
      if (!selected?.currentSessionId || !transferTargetId) throw new Error('Выберите целевой ПК')
      const target = (computersQuery.data ?? []).find((c) => c.id === transferTargetId)
      if (target && pcCanWake(target) && isOfflineFreePc(target)) {
        await apiFetch(`/api/computers/${target.id}/wake`, { method: 'POST', body: '{}' })
        await new Promise((r) => setTimeout(r, 1200))
      }
      return apiFetch(`/api/sessions/${selected.currentSessionId}/transfer`, {
        method: 'POST',
        body: JSON.stringify({
          targetComputerId: transferTargetId,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: () => {
      setTransferOpen(false)
      setTransferTargetId('')
      setSessionError(null)
      showFloorResult('Сеанс перенесён', 'Сеанс успешно перенесён на другой ПК. Если ПК был выключен — WOL-сигнал отправлен.')
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
      void queryClient.invalidateQueries({ queryKey: ['session-by-pc'] })
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка переноса'
      setSessionError(message)
      showFloorResult('Не удалось перенести сеанс', message, 'error')
    },
  })

  const layoutMutation = useMutation({
    mutationFn: async (payload: {
      id: string
      gridCol?: number
      gridRow?: number
      gridColSpan?: number
      gridRowSpan?: number
    }) =>
      apiFetch(`/api/computers/${payload.id}/layout`, {
        method: 'PUT',
        body: JSON.stringify({
          gridCol: payload.gridCol ?? null,
          gridRow: payload.gridRow ?? null,
          gridColSpan: payload.gridColSpan ?? null,
          gridRowSpan: payload.gridRowSpan ?? null,
        }),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
      void queryClient.invalidateQueries({ queryKey: ['floor-map'] })
    },
  })

  const createElementMutation = useMutation({
    mutationFn: async (body: {
      kind: FloorMapElementKind
      label?: string
      gridCol: number
      gridRow: number
      colSpan?: number
      rowSpan?: number
      endCol?: number
      endRow?: number
      colorHex?: string | null
      fillHex?: string | null
      strokeWidth?: number | null
      fontSize?: number | null
      sortOrder?: number
      showIcon?: boolean
      rotationDeg?: number
      isVisible?: boolean
    }) =>
      apiFetch<FloorMapElement>('/api/floor-map/elements', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['floor-map'] }),
  })

  const updateElementMutation = useMutation({
    mutationFn: async (payload: { id: string; body: Record<string, unknown> }) =>
      apiFetch<FloorMapElement>(`/api/floor-map/elements/${payload.id}`, {
        method: 'PUT',
        body: JSON.stringify(payload.body),
      }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['floor-map'] }),
  })

  const deleteElementMutation = useMutation({
    mutationFn: async (id: string) => apiFetch(`/api/floor-map/elements/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      setSelectedElementId(null)
      void queryClient.invalidateQueries({ queryKey: ['floor-map'] })
    },
  })

  const settingsMutation = useMutation({
    mutationFn: async (body: {
      floorGridCols: number
      floorGridRows: number
      floorBackgroundHex?: string | null
    }) =>
      apiFetch<FloorMapDto>('/api/floor-map/settings', {
        method: 'PUT',
        body: JSON.stringify(body),
      }),
    onSuccess: (res) => {
      if (res.data) queryClient.setQueryData(['floor-map'], res.data)
      void queryClient.invalidateQueries({ queryKey: ['floor-map'] })
      void queryClient.invalidateQueries({ queryKey: ['floor-map', 'display'] })
    },
  })

  function patchElementLocal(id: string, patch: Partial<FloorMapElement>) {
    queryClient.setQueryData<FloorMapDto>(['floor-map'], (prev) =>
      prev
        ? {
            ...prev,
            elements: prev.elements.map((e) => (e.id === id ? { ...e, ...patch } : e)),
          }
        : prev,
    )
  }

  function saveElement(el: FloorMapElement, patch: Parameters<typeof elementUpsertBody>[1] = {}) {
    const body = elementUpsertBody(el, patch)
    patchElementLocal(el.id, {
      label: body.label as string | null | undefined,
      gridCol: body.gridCol as number,
      gridRow: body.gridRow as number,
      colSpan: body.colSpan as number,
      rowSpan: body.rowSpan as number,
      endCol: body.endCol as number | null | undefined,
      endRow: body.endRow as number | null | undefined,
      colorHex: body.colorHex as string | null | undefined,
      fillHex: body.fillHex as string | null | undefined,
      strokeWidth: body.strokeWidth as number | null | undefined,
      fontSize: body.fontSize as number | null | undefined,
      sortOrder: body.sortOrder as number,
      showIcon: body.showIcon as boolean,
      rotationDeg: body.rotationDeg as number,
      isVisible: body.isVisible as boolean,
    })
    updateElementMutation.mutate({ id: el.id, body })
  }

  const startSessionMutation = useMutation({
    mutationFn: async () => {
      if (!tariffId) throw new Error('Выберите тариф')
      setSessionError(null)

      const customerId = startCustomerId || null
      const spendBank = useTimeBank && !!customerId && !startFromBookingId
      const startAmountDue =
        spendBank && canUseTimeBank ? startBarTotal : startSessionDue + startBarTotal
      if (startAmountDue > 0 && !paymentMethod) throw new Error('Выберите способ оплаты')
      const payMethod = paymentMethod ?? (spendBank ? 'Free' : 'Cash')
      const mixedGrand =
        payMethod === 'Mixed' ? buildMixedPayments(startAmountDue, startMixedCash) : null
      if (payMethod === 'Mixed' && !mixedGrand)
        throw new Error('Смешанная: укажите наличные (остаток уйдёт в Kaspi), обе части > 0')

      const sessionDueNow = spendBank && canUseTimeBank ? 0 : startSessionDue
      const barDueNow = startBarTotal
      let sessionPayFinal: string = spendBank ? 'Free' : payMethod
      let sessionPayments: { method: string; amount: number }[] | undefined
      let barPayFinal: string = paymentMethod ?? 'Cash'
      let barPayments: { method: string; amount: number }[] | undefined

      if (payMethod === 'Mixed' && mixedGrand) {
        const cashAll = mixedGrand.find((p) => p.method === 'Cash')!.amount
        const cashForSession = Math.min(cashAll, sessionDueNow)
        const kaspiForSession = Math.max(0, sessionDueNow - cashForSession)
        const cashForBar = Math.max(0, cashAll - cashForSession)
        const kaspiForBar = Math.max(0, barDueNow - cashForBar)

        if (sessionDueNow > 0) {
          if (cashForSession > 0 && kaspiForSession > 0) {
            sessionPayFinal = 'Mixed'
            sessionPayments = [
              { method: 'Cash', amount: cashForSession },
              { method: 'KaspiQr', amount: kaspiForSession },
            ]
          } else if (kaspiForSession > 0) {
            sessionPayFinal = 'KaspiQr'
          } else {
            sessionPayFinal = 'Cash'
          }
        }

        if (barDueNow > 0) {
          if (cashForBar > 0 && kaspiForBar > 0) {
            barPayFinal = 'Mixed'
            barPayments = [
              { method: 'Cash', amount: cashForBar },
              { method: 'KaspiQr', amount: kaspiForBar },
            ]
          } else if (kaspiForBar > 0) {
            barPayFinal = 'KaspiQr'
          } else {
            barPayFinal = 'Cash'
          }
        }
      } else if (spendBank) {
        barPayFinal =
          paymentMethod === 'Balance' ? 'Balance' : (paymentMethod ?? 'Cash')
      }

      const kaspiCharge =
        payMethod === 'KaspiQr'
          ? startAmountDue
          : payMethod === 'Mixed'
            ? (mixedGrand?.find((p) => p.method === 'KaspiQr')?.amount ?? 0)
            : 0
      if (!spendBank && kaspiCharge > 0) {
        await payKaspiTerminalIfReady(kaspiCharge)
      } else if (spendBank && payMethod === 'KaspiQr' && barDueNow > 0) {
        await payKaspiTerminalIfReady(barDueNow)
      } else if (spendBank && payMethod === 'Mixed' && barPayments) {
        const k = barPayments.find((p) => p.method === 'KaspiQr')?.amount ?? 0
        if (k > 0) await payKaspiTerminalIfReady(k)
      }
      if (!spendBank && paymentMethod === 'Balance' && !customerId)
        throw new Error('Выберите клиента для оплаты с баланса')
      if (customerId && startDialogTargets.length > 1 && !startFromBookingId)
        throw new Error('Аккаунт клиента — только на один ПК. Снимите клиента или выберите один компьютер.')

      const guestName =
        startCustomer?.fullName?.trim() ||
        startCustomerQuery.trim() ||
        selectedBooking?.contactName ||
        null

      type SessionLiteDto = { id: string; computerId: string }
      let sessions: SessionLiteDto[] = []
      let batchErrors: string[] = []
      const tariffForStart = availableTariffs.find((x) => x.id === tariffId) ?? null
      const billMinutes = resolveEffectiveStartMinutes(tariffForStart, durationMinutes)
      if (!spendBank && billMinutes <= 0) throw new Error('Укажите длительность сеанса')
      const startMinutes = spendBank
        ? Math.min(durationMinutes || billMinutes, startCustomerBank || durationMinutes || billMinutes)
        : billMinutes

      if (paymentMethod === 'Balance' && startBarCart.length > 0) {
        const wallet =
          Number(startCustomer?.balance ?? 0) + Number(startCustomer?.bonusBalance ?? 0)
        const due = startSessionDue + cartTotal(startBarCart)
        if (wallet + 0.001 < due)
          throw new Error(
            `Недостаточно на балансе: нужно ${due.toLocaleString('ru-RU')} ₸, есть ${wallet.toLocaleString('ru-RU')} ₸`,
          )
      }

      const wakeTargets =
        wakeBeforeStart && !startFromBookingId
          ? startDialogTargets.filter((c) => pcIsOfflineFree(c) && pcCanWake(c))
          : startFromBookingId && selected && wakeBeforeStart && pcIsOfflineFree(selected) && pcCanWake(selected)
            ? [selected]
            : []

      if (wakeTargets.length > 0) {
        const results = await Promise.allSettled(
          wakeTargets.map((c) =>
            apiFetch(`/api/computers/${c.id}/wake`, { method: 'POST', body: '{}' }),
          ),
        )
        const failed = results.filter((r) => r.status === 'rejected')
        if (failed.length === wakeTargets.length) {
          const msg =
            failed[0]?.status === 'rejected' && failed[0].reason instanceof Error
              ? failed[0].reason.message
              : 'Не удалось отправить включение ПК'
          throw new Error(msg)
        }
        // Даём сети/BIOS секунду после magic packet, затем стартуем сеанс.
        await new Promise((r) => setTimeout(r, 1200))
      }

      if (startFromBookingId && selectedId) {
        const res = await apiFetch<SessionLiteDto>('/api/sessions/guest', {
          method: 'POST',
          body: JSON.stringify({
            computerId: selectedId,
            tariffId,
            durationMinutes: startMinutes,
            paymentMethod: sessionPayFinal,
            payments: sessionPayments,
            guestName: selectedBooking?.contactName ?? null,
            customerId,
            useTimeBank: false,
            bookingId: startFromBookingId,
            idempotencyKey: newIdempotencyKey(),
          }),
        })
        if (res.data) sessions = [res.data]
      } else {
        const targets = startDialogTargets.map((c) => c.id)
        if (targets.length === 0) throw new Error('Нет доступных ПК для старта')
        if (spendBank && targets.length > 1) throw new Error('Банк времени — только на один ПК')

        if (targets.length === 1) {
          const res = await apiFetch<SessionLiteDto>('/api/sessions/guest', {
            method: 'POST',
            body: JSON.stringify({
              computerId: targets[0],
              tariffId,
              durationMinutes: startMinutes,
              paymentMethod: sessionPayFinal,
              payments: sessionPayments,
              guestName,
              customerId,
              useTimeBank: spendBank,
              bookingId: null,
              idempotencyKey: newIdempotencyKey(),
            }),
          })
          if (res.data) sessions = [res.data]
        } else if (payMethod === 'Mixed' && sessionDueNow > 0) {
          const allSessionParts =
            sessionPayFinal === 'Mixed' && sessionPayments
              ? sessionPayments
              : [{ method: sessionPayFinal, amount: sessionDueNow }]
          const perPcSessionParts = dividePaymentParts(allSessionParts, targets.length)
          for (let i = 0; i < targets.length; i++) {
            const unit = resolveUnitPaymentFromParts(perPcSessionParts[i] ?? [])
            const pc = startDialogTargets[i]
            const name = pc?.displayName ?? pc?.windowsName ?? targets[i]
            try {
              const res = await apiFetch<SessionLiteDto>('/api/sessions/guest', {
                method: 'POST',
                body: JSON.stringify({
                  computerId: targets[i],
                  tariffId,
                  durationMinutes: startMinutes,
                  paymentMethod: unit.paymentMethod,
                  payments: unit.payments,
                  guestName,
                  customerId,
                  useTimeBank: false,
                  bookingId: null,
                  idempotencyKey: newIdempotencyKey(),
                }),
              })
              if (res.data) sessions.push(res.data)
            } catch (e) {
              const msg = e instanceof Error ? e.message : 'ошибка'
              batchErrors.push(`${name}: ${msg}`)
            }
          }
        } else {
          const res = await apiFetch<{ sessions: SessionLiteDto[]; errors: string[] }>(
            '/api/sessions/guest/batch',
            {
            method: 'POST',
            body: JSON.stringify({
              computerIds: targets,
              tariffId,
              durationMinutes: startMinutes,
              paymentMethod: payMethod,
              guestName,
              customerId,
              useTimeBank: false,
              idempotencyKey: newIdempotencyKey(),
            }),
          },
          )
          sessions = res.data?.sessions ?? []
          batchErrors = res.data?.errors ?? []
        }
      }

      let barWarning: string | null = null
      if (startBarCart.length > 0 && sessions.length > 0) {
        const s = sessions[0]
        try {
          await sellBarItemsNow({
            computerId: s.computerId,
            gamingSessionId: s.id,
            customerId,
            cart: startBarCart,
            paymentMethod: barPayFinal as 'Cash' | 'KaspiQr' | 'Balance' | 'Mixed',
            payments: barPayments,
            skipKaspiTerminal: true,
            comment:
              sessions.length > 1
                ? `Бар при старте компании (${sessions.length} ПК)`
                : 'Бар при старте сеанса',
          })
        } catch (e) {
          barWarning =
            e instanceof Error
              ? e.message
              : 'Не удалось продать бар'
        }
      }

      return { sessions, startMinutes, barWarning, batchErrors }
    },
    onSuccess: async (result) => {
      const sessions = result.sessions
      const mins = result.startMinutes
      setStartOpen(false)
      setStartFromBookingId(null)
      setStartCustomerId('')
      setStartCustomerQuery('')
      setStartCustomerPicked(null)
      setUseTimeBank(false)
      setWakeBeforeStart(true)
      setStartBarCart([])
      setStartPaidAmount('')
      setStartCashTendered('')
      setPaymentMethod(null)
      const batchNote =
        result.batchErrors.length > 0
          ? ` Запущено ${sessions.length} из ${sessions.length + result.batchErrors.length}. ${result.batchErrors.join('; ')}`
          : ''
      if (result.barWarning) {
        showFloorResult(
          'Сеанс запущен частично',
          `Сеанс стартовал, но бар не продан: ${result.barWarning}. Продайте товары через «Бар».${batchNote}`,
          'warning',
        )
      } else if (result.batchErrors.length > 0) {
        showFloorResult(
          'Сеанс запущен частично',
          `Запущено ${sessions.length} ПК. Не удалось: ${result.batchErrors.join('; ')}`,
          'warning',
        )
      } else {
        showFloorResult(
          'Сеанс запущен',
          sessions.length > 1 ? `Запущено ${sessions.length} сеанса(ов).` : 'Сеанс успешно запущен.',
        )
      }
      if (sessions.length > 0) {
        queryClient.setQueryData<ComputerDto[]>(['computers'], (prev) =>
          (prev ?? []).map((c) => {
            const hit = sessions.find((s) => s.computerId === c.id)
            if (!hit) return c
            return {
              ...c,
              currentSessionId: hit.id,
              occupancy: 'Busy',
              status: 'InSession',
              remainingSeconds: mins * 60,
            }
          }),
        )
      }
      await refreshFloorLive()
      void queryClient.invalidateQueries({ queryKey: ['bar-orders'] })
      void queryClient.invalidateQueries({ queryKey: ['bar-products'] })
      void queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      void queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
      void queryClient.invalidateQueries({ queryKey: ['customers'] })
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка запуска'
      showFloorResult('Не удалось запустить сеанс', message, 'error')
    },
  })

  const bookMutation = useMutation({
    mutationFn: async () => {
      const pcs = selectedComputers.filter((c) => c.isApproved).map((c) => c.id)
      if (pcs.length === 0) throw new Error('Выберите ПК')
      if (!bookContactName.trim() || !bookContactPhone.trim()) throw new Error('Имя и телефон обязательны')
      return apiFetch('/api/bookings', {
        method: 'POST',
        body: JSON.stringify({
          contactName: bookContactName.trim(),
          contactPhone: bookContactPhone.trim(),
          comment: bookComment || null,
          localDate: bookDate,
          localTime: bookTime,
          startsAt: null,
          durationMinutes: bookDuration,
          computerIds: pcs,
          prepaidAmount: bookPrepaid,
          prepayMethod: bookPrepaid > 0 ? bookPrepayMethod : null,
          graceMinutes: 15,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: () => {
      setBookOpen(false)
      setBookContactName('')
      setBookContactPhone('')
      setBookComment('')
      setBookPrepaid(0)
      setSessionError(null)
      showFloorResult('Бронь создана', 'Бронь успешно создана и появилась на карте зала.')
      void queryClient.invalidateQueries({ queryKey: ['bookings'] })
      void queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка брони'
      setSessionError(message)
      showFloorResult('Не удалось создать бронь', message, 'error')
    },
  })

  const cancelBookingThenStartMutation = useMutation({
    mutationFn: async (bookingId: string) => {
      await apiFetch(`/api/bookings/${bookingId}/cancel`, {
        method: 'POST',
        body: JSON.stringify({ reason: 'Снята при старте сеанса с кассы' }),
      })
    },
    onSuccess: async () => {
      setBookingConflict(null)
      setStartFromBookingId(null)
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
      setStartOpen(true)
    },
    onError: (err) =>
      showFloorResult(
        'Не удалось отменить бронь',
        err instanceof Error ? err.message : 'Не удалось отменить бронь',
        'error',
      ),
  })

  const cancelBookingOnlyMutation = useMutation({
    mutationFn: async (bookingId: string) =>
      apiFetch(`/api/bookings/${bookingId}/cancel`, {
        method: 'POST',
        body: JSON.stringify({ reason: 'Отмена с карты зала' }),
      }),
    onSuccess: async () => {
      setBookingConflict(null)
      setSessionError(null)
      showFloorResult('Бронь отменена', 'Бронь успешно отменена.')
      await queryClient.invalidateQueries({ queryKey: ['bookings'] })
      await queryClient.invalidateQueries({ queryKey: ['computers'] })
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка отмены'
      setSessionError(message)
      showFloorResult('Не удалось отменить бронь', message, 'error')
    },
  })

  const extendMutation = useMutation({
    mutationFn: async () => {
      const targets = extendTargets
      if (targets.length === 0) throw new Error('Нет активного сеанса')
      if (extendBillMinutes <= 0) throw new Error('Укажите длительность продления')
      if (extendExpected > 0 && !extendPay) throw new Error('Выберите способ оплаты')
      if (extendPay === 'Balance' && targets.length > 1)
        throw new Error('Оплата с баланса — только на один ПК. Выберите один компьютер или другой способ оплаты.')
      const perPcDue = extendPerPcExpected
      const extendMixedGrand =
        extendPay === 'Mixed' ? buildMixedPayments(extendExpected, extendMixedCash) : null
      if (extendPay === 'Mixed' && !extendMixedGrand)
        throw new Error('Смешанная: укажите наличные (остаток уйдёт в Kaspi), обе части > 0')
      const extendMixedPerPc =
        extendMixedGrand && targets.length > 0
          ? dividePaymentParts(extendMixedGrand, targets.length)
          : null
      const kaspiExtend =
        extendPay === 'KaspiQr'
          ? extendExpected
          : extendPay === 'Mixed'
            ? (extendMixedGrand?.find((p) => p.method === 'KaspiQr')?.amount ?? 0)
            : 0
      if (kaspiExtend > 0) {
        await payKaspiTerminalIfReady(kaspiExtend)
      }

      const errors: string[] = []
      let ok = 0

      for (let i = 0; i < targets.length; i++) {
        const pc = targets[i]
        if (!pc.currentSessionId) continue
        const sessionTid =
          targets.length === 1 ? (activeSessionQuery.data?.tariffId ?? null) : null
        const payload = resolveExtendApiPayload(extendTariff, sessionTid, extendBillMinutes)
        const customerId =
          targets.length === 1
            ? (activeSessionQuery.data?.customerId ?? pc.sessionCustomerId ?? null)
            : (pc.sessionCustomerId ?? null)
        const unitPay =
          extendPay === 'Mixed' && extendMixedPerPc
            ? resolveUnitPaymentFromParts(extendMixedPerPc[i] ?? [])
            : { paymentMethod: extendPay ?? 'Cash', payments: undefined as undefined }
        const payForTendered: FloorPayMethod | null =
          unitPay.paymentMethod === 'Mixed'
            ? 'Mixed'
            : unitPay.paymentMethod === 'KaspiQr'
              ? 'KaspiQr'
              : unitPay.paymentMethod === 'Cash'
                ? 'Cash'
                : extendPay
        try {
          await apiFetch(`/api/sessions/${pc.currentSessionId}/extend`, {
            method: 'POST',
            body: JSON.stringify({
              additionalMinutes: payload.additionalMinutes,
              tariffId: payload.tariffId,
              paymentMethod: unitPay.paymentMethod,
              payments: unitPay.payments,
              amountTendered: resolveFloorAmountTendered(
                payForTendered,
                perPcDue,
                extendPay === 'Cash' ? String(Math.round(perPcDue)) : extendTendered,
              ),
              customerId,
              idempotencyKey: newIdempotencyKey(),
            }),
          })
          ok += 1
        } catch (e) {
          const name = pc.displayName ?? pc.windowsName
          const msg = e instanceof Error ? e.message : 'ошибка'
          errors.push(`${name}: ${msg}`)
        }
      }

      if (ok === 0) throw new Error(errors[0] ?? 'Не удалось продлить')
      return { ok, errors, total: targets.length }
    },
    onSuccess: async (res) => {
      setExtendOpen(false)
      setExtendTariffId(null)
      setExtendPaidAmount('')
      setExtendPay(null)
      setSessionError(null)
      if (res.errors.length > 0) {
        showFloorResult(
          `Продлено ${res.ok} из ${res.total}`,
          res.errors.join(' · '),
          'error',
        )
      } else {
        showFloorResult(
          res.ok > 1 ? `Продлено ${res.ok} ПК` : 'Сеанс продлён',
          res.ok > 1 ? `Продление применено на ${res.ok} компьютерах.` : 'Продление успешно применено.',
        )
      }
      await refreshFloorLive()
      void queryClient.invalidateQueries({ queryKey: ['cash-receipts'] })
      void queryClient.invalidateQueries({ queryKey: ['cash-shift-current'] })
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка продления'
      setSessionError(message)
      showFloorResult('Не удалось продлить сеанс', message, 'error')
    },
  })

  const sessionTariff = useMemo(() => {
    const tid = activeSessionQuery.data?.tariffId
    if (!tid) return null
    return (tariffsQuery.data ?? []).find((t) => t.id === tid) ?? null
  }, [activeSessionQuery.data?.tariffId, tariffsQuery.data])

  const startTariff = useMemo(
    () => availableTariffs.find((t) => t.id === tariffId) ?? null,
    [availableTariffs, tariffId],
  )

  function quoteMinutesList(t: TariffDto | null | undefined, minutes: number) {
    if (!t || minutes <= 0) return 0
    // Считаем по фактическим минутам сеанса (после resolveEffectiveStartMinutes у вызывающего).
    const m = minutes
    if (isTimeWindowTariff(t) && t.fixedPrice != null) {
      const rem = t.remainingMinutesInWindow ?? 0
      if (rem > 0 && m > 0 && m < rem) {
        if (t.pricePerHour > 0) {
          return Math.round((t.pricePerHour / 60) * m * 100) / 100
        }
        return Math.round((t.fixedPrice * m) / rem * 100) / 100
      }
      return t.fixedPrice
    }
    const kind = resolveTariffKind(t)
    if (kind === 'Package' && t.fixedPrice != null) {
      const fixed = t.fixedDurationMinutes ?? m
      if (m <= fixed) return t.fixedPrice
      const extra = m - fixed
      const rate = t.pricePerHour > 0 ? t.pricePerHour / 60 : 0
      return Math.round((t.fixedPrice + rate * extra) * 100) / 100
    }
    if (kind === 'Free') return 0
    const rate =
      t.pricePerHour > 0
        ? t.pricePerHour / 60
        : t.fixedPrice && t.fixedDurationMinutes
          ? t.fixedPrice / t.fixedDurationMinutes
          : 0
    let billable = m
    const mode = resolveBillingMode(t)
    if (mode === 'Block15') billable = Math.ceil(m / 15) * 15
    if (mode === 'Block30') billable = Math.ceil(m / 30) * 30
    let price = Math.round(rate * billable * 100) / 100
    if (t.minCharge > price) price = t.minCharge
    return price
  }

  /** Сколько минут даёт сумма с учётом акции тарифа и лояльности клиента. */
  function minutesFromPaidAmount(
    t: TariffDto | null | undefined,
    amount: number,
    loyaltyPercent = 0,
  ) {
    if (!t || amount <= 0) return 0

    const rem =
      isTimeWindowTariff(t) && (t.remainingMinutesInWindow ?? 0) > 0
        ? t.remainingMinutesInWindow!
        : 0
    const maxCap = rem > 0 ? rem : Math.min(t.maxDurationMinutes || 12 * 60, 12 * 60)
    if (maxCap <= 0) return 0

    // Полное окно дня/ночи, если хватает суммы уже со скидками
    if (rem > 0) {
      const fullCost = quoteMinutes(t, rem, loyaltyPercent)
      if (amount + 0.001 >= fullCost) return rem
    }

    // Пакет 2+1 / 3+2: только фикс. длительность пакета, без «лишних» минут по ₸/ч
    if (isPackageTariff(t) && !isTimeWindowTariff(t) && t.fixedDurationMinutes && t.fixedPrice != null) {
      const packMins = t.fixedDurationMinutes
      const packCost = quoteMinutes(t, packMins, loyaltyPercent)
      if (amount + 0.001 < packCost) return 0
      return packMins
    }

    // Макс. минуты, у которых цена со скидками ≤ суммы (монотонный поиск)
    let best = 0
    let lo = 1
    let hi = maxCap
    while (lo <= hi) {
      const mid = (lo + hi) >> 1
      const cost = quoteMinutes(t, mid, loyaltyPercent)
      if (cost <= amount + 0.001) {
        best = mid
        lo = mid + 1
      } else {
        hi = mid - 1
      }
    }
    return best
  }

  function quoteMinutes(t: TariffDto | null | undefined, minutes: number, loyaltyPercent = 0) {
    const list = quoteMinutesList(t, minutes)
    return applySessionDiscounts(list, Number(t?.promoPercent ?? 0), loyaltyPercent)
  }

  function formatDurationHuman(minutes: number) {
    return formatDurationChip(minutes)
  }

  function formatTariffTitle(t: TariffDto) {
    const code = (t.code ?? '').toUpperCase()
    if (code.includes('2P1') || t.name === '2+1') return '2+1'
    if (code.includes('3P2') || t.name === '3+2') return '3+2'
    return t.name
  }

  function formatTariffPrice(t: TariffDto) {
    const promo = Number(t.promoPercent ?? 0)
    if (isPackageTariff(t) && t.fixedPrice != null) {
      const sale = applyPercentDiscount(t.fixedPrice, promo)
      if (promo > 0 && sale < t.fixedPrice) {
        return `${sale.toLocaleString('ru-RU')} ₸`
      }
      return `${t.fixedPrice.toLocaleString('ru-RU')} ₸`
    }
    if (resolveTariffKind(t) === 'Free') return 'бесплатно'
    const hour = applyPercentDiscount(t.pricePerHour, promo)
    if (promo > 0 && hour < t.pricePerHour) {
      return `${hour.toLocaleString('ru-RU')} ₸/ч`
    }
    return `${t.pricePerHour.toLocaleString('ru-RU')} ₸/ч`
  }

  function tariffPromoHint(t: TariffDto) {
    const bits: string[] = []
    const promo = Number(t.promoPercent ?? 0)
    if (promo > 0) {
      bits.push(t.promoLabel?.trim() || `−${promo}%`)
    }
    if (isTimeWindowTariff(t)) {
      const from = t.availableFrom ?? '—'
      const to = t.windowEndsAtLocal ?? t.availableTo ?? '—'
      if (t.remainingMinutesInWindow != null) {
        bits.push(`до ${to} · осталось ${formatDurationChip(t.remainingMinutesInWindow)}`)
      } else {
        bits.push(`${from}–${to}`)
      }
      return bits.join(' · ')
    }
    const code = (t.code ?? '').toUpperCase()
    if (code.includes('2P1') || t.name === '2+1') bits.push('2 ч оплата + 1 ч бесплатно')
    else if (code.includes('3P2') || t.name === '3+2') bits.push('3 ч оплата + 2 ч бесплатно')
    else if (code.includes('_DAY') || t.name.toLowerCase() === 'день') bits.push('12:00–18:00')
    else if (code.includes('_NIGHT') || t.name.toLowerCase() === 'ночь') bits.push('23:00–08:00')
    else if (resolveTariffKind(t) === 'Hourly') bits.push('ровно 1 час')
    else if (isPackageTariff(t) && t.fixedDurationMinutes) {
      bits.push(formatDurationChip(t.fixedDurationMinutes))
    }
    return bits.length > 0 ? bits.join(' · ') : null
  }

  const extendAvailableTariffs = useMemo(() => {
    const all = (tariffsQuery.data ?? []).filter((t) => t.isActive && t.isAvailableNow !== false)
    const zoneId = selected?.zoneId ?? activeSessionQuery.data?.zoneId
    if (zoneId) return all.filter((t) => !t.zoneId || t.zoneId === zoneId)
    return all
  }, [tariffsQuery.data, selected?.zoneId, activeSessionQuery.data?.zoneId])

  const extendTariff = useMemo(
    () => extendAvailableTariffs.find((t) => t.id === extendTariffId) ?? null,
    [extendAvailableTariffs, extendTariffId],
  )

  const extendCustomerQuery = useQuery({
    queryKey: ['customer-extend', activeSessionQuery.data?.customerId],
    queryFn: async () =>
      (await apiFetch<FloorCustomerLite>(`/api/customers/${activeSessionQuery.data!.customerId}`)).data ?? null,
    enabled: extendOpen && !!activeSessionQuery.data?.customerId,
    staleTime: 30_000,
  })

  const extendCustomer = extendCustomerQuery.data ?? null
  const extendLoyaltyPercent = Number(extendCustomer?.loyaltyTimeDiscountPercent ?? 0)
  const extendPcCount = Math.max(1, extendTargets.length || (selected?.currentSessionId ? 1 : 0))
  const extendBillMinutes = resolveEffectiveStartMinutes(extendTariff, extendMinutes)
  const extendListExpected =
    quoteMinutesList(extendTariff, extendBillMinutes) * extendPcCount
  const extendPerPcExpected = quoteMinutes(
    extendTariff,
    extendBillMinutes,
    extendPcCount === 1 ? extendLoyaltyPercent : 0,
  )
  const extendExpected = Math.round(extendPerPcExpected * extendPcCount * 100) / 100
  const extendDiscount = Math.max(0, Math.round((extendListExpected - extendExpected) * 100) / 100)
  const extendDiscountBits = useMemo(() => {
    const bits: string[] = []
    const promo = Number(extendTariff?.promoPercent ?? 0)
    if (promo > 0) bits.push(extendTariff?.promoLabel?.trim() || `акция −${promo}%`)
    if (extendLoyaltyPercent > 0) {
      const label = extendCustomer?.loyaltyLevelName?.trim()
      bits.push(label ? `${label} −${extendLoyaltyPercent}%` : `лояльность −${extendLoyaltyPercent}%`)
    }
    return bits
  }, [extendTariff, extendLoyaltyPercent, extendCustomer])
  const extendCanExtend = !!extendTariffId && extendBillMinutes > 0
  const extendPaymentRequired = extendExpected > 0
  const extendCashDue = extendPay === 'Cash' ? extendExpected : 0
  const extendCashChange =
    extendPay === 'Cash' && extendTendered !== '' && Number(extendTendered) > extendCashDue
      ? Math.round((Number(extendTendered) - extendCashDue) * 100) / 100
      : 0
  const extendCashShort =
    extendPay === 'Cash' &&
    extendCashDue > 0 &&
    (extendTendered === '' || Number(extendTendered) + 0.001 < extendCashDue)
  const extendMixedShort =
    extendPay === 'Mixed' &&
    extendExpected > 0 &&
    !buildMixedPayments(extendExpected, extendMixedCash)
  const extendPaidHintMins = useMemo(() => {
    const paid = Number(extendPaidAmount)
    if (!extendTariff || !Number.isFinite(paid) || paid <= 0) return 0
    return minutesFromPaidAmount(extendTariff, paid, extendLoyaltyPercent)
  }, [extendTariff, extendPaidAmount, extendLoyaltyPercent])

  const startPcCount = startFromBookingId ? 1 : Math.max(1, startDialogTargets.length || (selected ? 1 : 0))
  const startLoyaltyPercent = Number(startCustomer?.loyaltyTimeDiscountPercent ?? 0)
  const startBillMinutes = resolveEffectiveStartMinutes(startTariff, durationMinutes)
  const startListExpected =
    useTimeBank && canUseTimeBank
      ? 0
      : quoteMinutesList(startTariff, startBillMinutes) * startPcCount
  const startExpected =
    useTimeBank && canUseTimeBank
      ? 0
      : quoteMinutes(startTariff, startBillMinutes, startLoyaltyPercent) * startPcCount
  const startDiscount = Math.max(0, Math.round((startListExpected - startExpected) * 100) / 100)
  const startBookingPrepaid =
    startFromBookingId && selectedBooking && selectedBooking.prepaidAmount > 0
      ? selectedBooking.prepaidAmount
      : 0
  const startSessionDue = Math.max(0, startExpected - startBookingPrepaid)
  const startBarTotal = cartTotal(startBarCart)
  const startGrandTotal = startSessionDue + startBarTotal
  const startAmountDue = useTimeBank && canUseTimeBank ? startBarTotal : startGrandTotal
  const startPaymentRequired = startAmountDue > 0
  // Банк времени: сеанс 0 ₸, к кассе только бар (если наличные).
  const startCashDue =
    paymentMethod === 'Cash'
      ? useTimeBank && canUseTimeBank
        ? startBarTotal
        : startGrandTotal
      : 0
  const startCashChange =
    paymentMethod === 'Cash' &&
    startCashTendered !== '' &&
    Number(startCashTendered) > startCashDue
      ? Math.round((Number(startCashTendered) - startCashDue) * 100) / 100
      : 0
  const startCashShort =
    paymentMethod === 'Cash' &&
    startCashDue > 0 &&
    (startCashTendered === '' || Number(startCashTendered) + 0.001 < startCashDue)
  const startMixedShort =
    paymentMethod === 'Mixed' &&
    startAmountDue > 0 &&
    !buildMixedPayments(startAmountDue, startMixedCash)
  const startPaidHintMins = useMemo(() => {
    const paid = Number(startPaidAmount)
    if (!startTariff || !Number.isFinite(paid) || paid <= 0) return 0
    return minutesFromPaidAmount(startTariff, paid, startLoyaltyPercent)
  }, [startTariff, startPaidAmount, startLoyaltyPercent])

  // Prefill cash tendered with amount due when starting on cash
  useEffect(() => {
    if (!startOpen || paymentMethod !== 'Cash') return
    if (startCashDue <= 0) {
      setStartCashTendered('')
      return
    }
    setStartCashTendered(String(Math.round(startCashDue)))
  }, [startOpen, paymentMethod, startCashDue])

  useEffect(() => {
    if (!startOpen || paymentMethod !== 'Mixed' || startAmountDue <= 0) return
    setStartMixedCash((prev) =>
      prev === '' ? String(Math.round(startAmountDue / 2)) : prev,
    )
  }, [startOpen, paymentMethod, startAmountDue])

  useEffect(() => {
    if (!extendOpen || extendPay !== 'Mixed' || extendExpected <= 0) return
    setExtendMixedCash((prev) =>
      prev === '' ? String(Math.round(extendExpected / 2)) : prev,
    )
  }, [extendOpen, extendPay, extendExpected])

  // Если сменили клиента/тариф при введённой сумме — пересчитать минуты со скидками
  useEffect(() => {
    if (!startOpen || !startPaidAmount) return
    const paid = Number(startPaidAmount)
    if (!Number.isFinite(paid) || paid <= 0 || !startTariff) return
    const mins = minutesFromPaidAmount(startTariff, paid, startLoyaltyPercent)
    if (mins > 0 && mins !== durationMinutes) setDurationMinutes(mins)
  }, [startOpen, startTariff?.id, startLoyaltyPercent])

  const startDiscountBits = useMemo(() => {
    const bits: string[] = []
    const promo = Number(startTariff?.promoPercent ?? 0)
    if (promo > 0) bits.push(startTariff?.promoLabel?.trim() || `акция −${promo}%`)
    if (startLoyaltyPercent > 0) bits.push(`лояльность −${startLoyaltyPercent}%`)
    return bits
  }, [startTariff, startLoyaltyPercent])

  const extendChange = extendCashChange

  function pickExtendTariff(t: TariffDto) {
    setExtendTariffId(t.id)
    setExtendPaidAmount('')
    if (isTimeWindowTariff(t) && t.remainingMinutesInWindow && t.remainingMinutesInWindow > 0) {
      setExtendMinutes(t.remainingMinutesInWindow)
    } else if (isPackageTariff(t) && t.fixedDurationMinutes) {
      setExtendMinutes(t.fixedDurationMinutes)
    } else if (resolveTariffKind(t) === 'Hourly') {
      setExtendMinutes(t.minDurationMinutes && t.minDurationMinutes > 0 ? t.minDurationMinutes : 60)
    }
  }

  function openExtend(minutes: number) {
    const tid = activeSessionQuery.data?.tariffId ?? sessionTariff?.id ?? null
    setExtendTariffId(tid)
    setExtendMinutes(minutes)
    setExtendPaidAmount('')
    setExtendPay(null)
    setExtendTendered('')
    setSessionError(null)
    setExtendOpen(true)
  }

  useEffect(() => {
    if (!extendOpen) return
    const tid = activeSessionQuery.data?.tariffId
    if (tid && extendAvailableTariffs.some((t) => t.id === tid)) {
      if (!extendTariffId) setExtendTariffId(tid)
    } else if (extendAvailableTariffs.length && !extendTariffId) {
      setExtendTariffId(extendAvailableTariffs[0].id)
    }
  }, [extendOpen, activeSessionQuery.data?.tariffId, extendAvailableTariffs, extendTariffId])

  useEffect(() => {
    if (!extendOpen || extendPay !== 'Cash') return
    if (extendExpected <= 0) {
      setExtendTendered('')
      return
    }
    setExtendTendered(String(Math.round(extendExpected)))
  }, [extendOpen, extendPay, extendExpected])

  useEffect(() => {
    if (!extendOpen || !extendPaidAmount) return
    const paid = Number(extendPaidAmount)
    if (!Number.isFinite(paid) || paid <= 0 || !extendTariff) return
    const mins = minutesFromPaidAmount(extendTariff, paid, extendLoyaltyPercent)
    if (mins > 0 && mins !== extendMinutes) setExtendMinutes(mins)
  }, [extendOpen, extendTariff?.id, extendLoyaltyPercent, extendPaidAmount])

  const occupancyKind = selected ? resolveOccupancy(selected).kind : null
  const canStartFromBooking =
    selectedIds.length === 1 &&
    !!selectedBooking &&
    isOpenBookingStatus(selectedBooking.status) &&
    !!selectedBooking.computers.find((c) => c.computerId === selectedId && !c.gamingSessionId)

  const canStart =
    !!tariffId &&
    (canStartFromBooking ||
      startDialogTargets.length > 0 ||
      (!!selected &&
        selected.isApproved &&
        !selected.currentSessionId &&
        (occupancyKind === 'Free' || occupancyKind === 'Reserved')))

  function findWalkInConflict(computerIds: string[], durationMin: number): FloorBooking | null {
    for (const id of computerIds) {
      const b = bookingsByPc.get(id)
      if (!b) continue
      if (!b.computers.some((c) => c.computerId === id && !c.gamingSessionId)) continue
      if (explainBookingBlock(b, durationMin).blocked) return b
    }
    return null
  }

  const startWalkInConflict = useMemo(() => {
    if (startFromBookingId || !startOpen) return null
    const ids = startDialogTargets.map((c) => c.id)
    if (ids.length === 0 && selectedId) ids.push(selectedId)
    const b = findWalkInConflict(ids, durationMinutes)
    if (!b) return null
    return { booking: b, ...explainBookingBlock(b, durationMinutes) }
  }, [
    startFromBookingId,
    startOpen,
    startDialogTargets,
    selectedId,
    durationMinutes,
    bookingsByPc,
  ])

  function requestOpenStart(opts?: { fromBooking?: boolean }) {
    setSessionError(null)
    const booking = selectedBooking
    const wantFromBooking = !!opts?.fromBooking

    if (!wantFromBooking && selectedIds.length >= 1) {
      const conflictBooking = findWalkInConflict(selectedIds, durationMinutes)
      if (conflictBooking) {
        const explained = explainBookingBlock(conflictBooking, durationMinutes)
        // Soft-hold / клиент пришёл — walk-in нельзя, только бронь или отмена.
        if (explained.reason === 'soft-hold' || explained.reason === 'arrived') {
          setBookingConflict(conflictBooking)
          return
        }
        // overlap: откроем старт — можно выбрать меньший тариф
      }
    }

    if (wantFromBooking && booking) {
      setStartFromBookingId(booking.id)
      setDurationMinutes(booking.durationMinutes)
      if (booking.customerId) {
        setStartCustomerId(booking.customerId)
        setStartCustomerQuery(booking.customerName ?? booking.contactName)
        setStartCustomerPicked({
          id: booking.customerId,
          fullName: booking.customerName ?? booking.contactName,
          phone: booking.contactPhone,
          balance: 0,
        })
      } else {
        setStartCustomerId('')
        setStartCustomerQuery('')
        setStartCustomerPicked(null)
      }
    } else {
      setStartFromBookingId(null)
    }
    setPaymentMethod(null)
    setStartCustomerId('')
    setStartCustomerQuery('')
    setStartCustomerPicked(null)
    setUseTimeBank(false)
    setStartBarCart([])
    setStartCashTendered('')
    setWakeBeforeStart(true)
    setStartOpen(true)
  }

  const endMutation = useMutation({
    mutationFn: async (opts?: { saveRemaining?: boolean }) => {
      if (!selected?.currentSessionId) throw new Error('Нет активного сеанса')
      return apiFetch(`/api/sessions/${selected.currentSessionId}/end`, {
        method: 'POST',
        body: JSON.stringify({
          forceUnpaid: false,
          saveRemainingToTimeBank: !!opts?.saveRemaining,
        }),
      })
    },
    onSuccess: async () => {
      setEndConfirm(null)
      showFloorResult('Сеанс завершён', 'Сеанс успешно завершён.')
      await refreshFloorLive({ clearSessionPcId: selectedId })
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка завершения'
      setSessionError(message)
      showFloorResult('Не удалось завершить сеанс', message, 'error')
    },
  })

  const attachMutation = useMutation({
    mutationFn: async () => {
      if (!selected?.currentSessionId) throw new Error('Нет активного сеанса')
      if (!attachPicked) throw new Error('Выберите клиента')
      await apiFetch(`/api/sessions/${selected.currentSessionId}/attach-customer`, {
        method: 'POST',
        body: JSON.stringify({ customerId: attachPicked.id }),
      })
      if (attachThenSave) {
        try {
          await apiFetch(`/api/sessions/${selected.currentSessionId}/end`, {
            method: 'POST',
            body: JSON.stringify({
              forceUnpaid: false,
              saveRemainingToTimeBank: true,
            }),
          })
        } catch (e) {
          const msg = e instanceof Error ? e.message : 'Не удалось завершить сеанс'
          throw new Error(`Клиент привязан, но завершение не удалось: ${msg}`)
        }
      }
    },
    onSuccess: async () => {
      setAttachOpen(false)
      setAttachPicked(null)
      setAttachQuery('')
      setSessionError(null)
      showFloorResult(
        attachThenSave ? 'Сеанс сохранён в аккаунт' : 'Аккаунт привязан',
        attachThenSave
          ? 'Клиент привязан, сеанс завершён, остаток минут сохранён в банк.'
          : 'Гостевой сеанс привязан к аккаунту клиента.',
      )
      if (attachThenSave) {
        await refreshFloorLive({ clearSessionPcId: selectedId })
      } else {
        await refreshFloorLive()
        void queryClient.invalidateQueries({ queryKey: ['session-by-pc'] })
      }
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка привязки'
      const attachedOnly = message.includes('Клиент привязан, но')
      showFloorResult(
        attachedOnly ? 'Аккаунт привязан частично' : 'Не удалось привязать аккаунт',
        message,
        attachedOnly ? 'warning' : 'error',
      )
      if (attachedOnly) {
        void refreshFloorLive()
        void queryClient.invalidateQueries({ queryKey: ['session-by-pc'] })
      }
    },
  })

  function openAttach(thenSave: boolean) {
    setAttachThenSave(thenSave)
    setAttachQuery('')
    setAttachPicked(null)
    setSessionError(null)
    setAttachOpen(true)
  }

  const pauseMutation = useMutation({
    mutationFn: async () => {
      if (!selected?.currentSessionId) throw new Error('Нет активного сеанса')
      return apiFetch(`/api/sessions/${selected.currentSessionId}/pause`, { method: 'POST', body: '{}' })
    },
    onSuccess: async () => {
      showFloorResult('Сеанс на паузе', 'Пауза успешно включена.')
      await refreshFloorLive()
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка паузы'
      setSessionError(message)
      showFloorResult('Не удалось поставить сеанс на паузу', message, 'error')
    },
  })

  const resumeMutation = useMutation({
    mutationFn: async () => {
      if (!selected?.currentSessionId) throw new Error('Нет сеанса')
      return apiFetch(`/api/sessions/${selected.currentSessionId}/resume`, { method: 'POST', body: '{}' })
    },
    onSuccess: async () => {
      showFloorResult('Сеанс возобновлён', 'Сеанс успешно снят с паузы.')
      await refreshFloorLive()
    },
    onError: (err) => {
      const message = err instanceof Error ? err.message : 'Ошибка возобновления'
      setSessionError(message)
      showFloorResult('Не удалось возобновить сеанс', message, 'error')
    },
  })

  function onFloorPcDrop(
    pcId: string,
    col: number,
    row: number,
    opts?: { swapWithId?: string },
  ) {
    if (!layoutEdit) return
    skipClickRef.current = true

    const list = computersQuery.data ?? []
    const moving = list.find((c) => c.id === pcId)
    const swapId = opts?.swapWithId
    const swapTarget = swapId ? list.find((c) => c.id === swapId) : undefined
    const prevCol = moving?.gridCol
    const prevRow = moving?.gridRow

    queryClient.setQueryData<ComputerDto[]>(['computers'], (prev) =>
      (prev ?? []).map((c) => {
        if (c.id === pcId) return { ...c, gridCol: col, gridRow: row }
        if (
          swapTarget &&
          c.id === swapTarget.id &&
          prevCol != null &&
          prevRow != null
        ) {
          return { ...c, gridCol: prevCol, gridRow: prevRow }
        }
        // Occupant sitting on this exact saved cell without explicit swap request
        if (!swapId && c.id !== pcId && c.gridCol === col && c.gridRow === row) {
          if (prevCol != null && prevRow != null) {
            return { ...c, gridCol: prevCol, gridRow: prevRow }
          }
        }
        return c
      }),
    )
    queryClient.setQueryData<FloorMapDto>(['floor-map'], (prev) =>
      prev
        ? {
            ...prev,
            computers: prev.computers.map((c) => {
              if (c.id === pcId) return { ...c, gridCol: col, gridRow: row }
              if (
                swapTarget &&
                c.id === swapTarget.id &&
                prevCol != null &&
                prevRow != null
              ) {
                return { ...c, gridCol: prevCol, gridRow: prevRow }
              }
              if (!swapId && c.id !== pcId && c.gridCol === col && c.gridRow === row) {
                if (prevCol != null && prevRow != null) {
                  return { ...c, gridCol: prevCol, gridRow: prevRow }
                }
              }
              return c
            }),
          }
        : prev,
    )

    layoutMutation.mutate({ id: pcId, gridCol: col, gridRow: row })
    if (swapTarget && prevCol != null && prevRow != null) {
      layoutMutation.mutate({ id: swapTarget.id, gridCol: prevCol, gridRow: prevRow })
    } else if (!swapId) {
      const occupant = list.find((c) => c.id !== pcId && c.gridCol === col && c.gridRow === row)
      if (occupant && prevCol != null && prevRow != null) {
        layoutMutation.mutate({ id: occupant.id, gridCol: prevCol, gridRow: prevRow })
      }
    }
  }

  function onFloorCellClick(col: number, row: number) {
    if (!layoutEdit || layoutTool === 'select') return
    if (layoutTool === 'Wall') {
      if (!wallDraft) {
        setWallDraft({ col, row })
        return
      }
      createElementMutation.mutate({
        kind: 'Wall',
        gridCol: wallDraft.col,
        gridRow: wallDraft.row,
        endCol: col,
        endRow: row,
        label: 'Стена',
        colorHex: defaultElementColor('Wall'),
        strokeWidth: 4,
        showIcon: false,
        rotationDeg: 0,
        sortOrder:
          Math.max(0, ...(floorMapQuery.data?.elements.map((e) => e.sortOrder ?? 0) ?? [0])) + 1,
      })
      setWallDraft(null)
      return
    }
    const label = layoutTool === 'Label' ? 'Подпись' : elementKindLabel(layoutTool)
    const maxOrder = Math.max(0, ...(floorMapQuery.data?.elements.map((e) => e.sortOrder ?? 0) ?? [0]))
    createElementMutation.mutate({
      kind: layoutTool,
      gridCol: col,
      gridRow: row,
      colSpan: layoutTool === 'Rect' ? 2 : 1,
      rowSpan: layoutTool === 'Rect' ? 2 : 1,
      label,
      colorHex: defaultElementColor(layoutTool),
      fillHex: layoutTool === 'Rect' || layoutTool === 'Label' ? defaultElementColor(layoutTool) : null,
      fontSize: layoutTool === 'Label' ? 16 : 14,
      strokeWidth: null,
      showIcon: layoutTool !== 'Label' && layoutTool !== 'Rect',
      rotationDeg: 0,
      sortOrder: maxOrder + 1,
    })
  }

  function onFloorElementClick(el: FloorMapElement) {
    if (!layoutEdit) return
    setSelectedElementId(el.id)
    setElementLabelDraft(el.label ?? '')
    setLayoutTool('select')
  }

  function onFloorElementMove(
    el: FloorMapElement,
    col: number,
    row: number,
    ends?: { endCol: number; endRow: number },
  ) {
    if (ends) {
      saveElement(el, { gridCol: col, gridRow: row, endCol: ends.endCol, endRow: ends.endRow })
      return
    }
    saveElement(el, { gridCol: col, gridRow: row })
  }

  function onFloorElementResize(el: FloorMapElement, colSpan: number, rowSpan: number) {
    saveElement(el, { colSpan, rowSpan })
  }

  function onFloorElementWallEndpoints(
    el: FloorMapElement,
    startCol: number,
    startRow: number,
    endCol: number,
    endRow: number,
  ) {
    saveElement(el, { gridCol: startCol, gridRow: startRow, endCol, endRow })
  }

  function nudgeElementLayer(el: FloorMapElement, direction: 'back' | 'front') {
    const list = [...(floorMapQuery.data?.elements ?? [])].sort(
      (a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0),
    )
    const idx = list.findIndex((e) => e.id === el.id)
    if (idx < 0) return
    const swapWith = direction === 'back' ? list[idx - 1] : list[idx + 1]
    if (!swapWith) return
    const aOrder = el.sortOrder ?? idx
    const bOrder = swapWith.sortOrder ?? (direction === 'back' ? idx - 1 : idx + 1)
    saveElement(el, { sortOrder: bOrder })
    saveElement(swapWith, { sortOrder: aOrder })
  }

  const selectedElement = useMemo(
    () => floorMapQuery.data?.elements.find((e) => e.id === selectedElementId) ?? null,
    [floorMapQuery.data, selectedElementId],
  )

  const isWallSelected = selectedElement
    ? (typeof selectedElement.kind === 'number'
        ? selectedElement.kind === 6
        : selectedElement.kind === 'Wall')
    : false

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Карта клуба</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Клик — выбрать · Ctrl/⌘ — несколько · справа: старт / продление
          </p>
        </div>
        <div className="action-chip-row">
          {selectedIds.length > 0 && (
            <>
              <span className="chip">Выбрано: {selectedIds.length}</span>
              <button
                type="button"
                disabled={
                  startDialogTargets.length === 0 &&
                  !canStartFromBooking &&
                  (!selected || !!selected.currentSessionId)
                }
                onClick={() =>
                  requestOpenStart(canStartFromBooking ? { fromBooking: true } : undefined)
                }
              >
                {canStartFromBooking ? 'Старт по брони' : 'Старт сеанса'}
                {!canStartFromBooking && startDialogTargets.length > 1
                  ? ` (${startDialogTargets.length})`
                  : ''}
              </button>
              <button
                type="button"
                className="ghost"
                disabled={extendTargets.length === 0}
                onClick={() => openExtend(30)}
              >
                Продлить
                {extendTargets.length > 1 ? ` (${extendTargets.length})` : ''}
              </button>
              <button
                type="button"
                className="ghost"
                disabled={selectedComputers.filter((c) => c.isApproved).length === 0}
                onClick={() => {
                  setSessionError(null)
                  setBookDate(todayLocalInput())
                  const now = new Date()
                  setBookTime(
                    `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`,
                  )
                  setBookOpen(true)
                }}
              >
                Создать бронь
              </button>
              <button
                type="button"
                className="ghost"
                disabled={techTargets.length === 0}
                onClick={() => {
                  setSessionError(null)
                  setTechCmdResult(null)
                  setTechOpen(true)
                }}
              >
                Техника
                {techTargets.length > 1 ? ` (${techTargets.length})` : ''}
              </button>
              <button type="button" className="ghost" onClick={() => setSelectedIds([])}>
                Снять
              </button>
            </>
          )}
          <button
            type="button"
            className="floor-alarm-btn"
            title="Экстренное сообщение поверх всех игр на всех ПК"
            disabled={voenkomatMutation.isPending}
            onClick={() => setVoenkomatOpen(true)}
          >
            {voenkomatMutation.isPending ? 'Отправка…' : 'ВОЕНКОМАТ'}
          </button>
          <a className="ghost" href="/display" target="_blank" rel="noreferrer">
            Экран ↗
          </a>
          {pendingPcCount > 0 && (
            <button type="button" className="ghost floor-pending-btn" onClick={() => setPendingPcsOpen(true)}>
              Новые ПК ({pendingPcCount})
            </button>
          )}
          {can(Perm.BarOrders) && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setOrdersHighlightId(null)
                setOrdersFilterPcId(null)
                setOrdersOpen(true)
              }}
            >
              Заказы{openBarOrderCount > 0 ? ` (${openBarOrderCount})` : ''}
            </button>
          )}
          {can(Perm.BarSell) && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setSessionError(null)
                setQuickBarOpen(true)
              }}
            >
              Бар
            </button>
          )}
          {can(Perm.CustomersDeposit) && (
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setSessionError(null)
                setQuickCaseKeyOpen(true)
              }}
            >
              Ключ CASE
            </button>
          )}
        </div>
      </header>
      <div className="map-layout">
        {computersQuery.isError && (
          <p className="error" style={{ gridColumn: '1 / -1' }}>
            {(computersQuery.error as Error).message}
          </p>
        )}
        <section className="map-panel">
          <div className="floor-toolbar">
            <div className="floor-view-toggle" role="group" aria-label="Вид зала">
              <button
                type="button"
                className={floorView === 'map' ? 'is-active' : undefined}
                onClick={() => setFloorViewMode('map')}
              >
                Карта
              </button>
              <button
                type="button"
                className={floorView === 'grid' ? 'is-active' : undefined}
                onClick={() => setFloorViewMode('grid')}
              >
                По зонам
              </button>
            </div>
            {canManagePcs && (
              <button
                type="button"
                className={`ghost${layoutEdit ? ' is-active' : ''}`}
                style={layoutEdit ? { borderColor: '#3b82f6' } : undefined}
                onClick={() => {
                  setLayoutEdit((v) => !v)
                  setWallDraft(null)
                  setLayoutTool('select')
                  setSelectedElementId(null)
                }}
              >
                {layoutEdit ? 'Готово' : 'Расстановка'}
              </button>
            )}
            <aside className="floor-legend-float" aria-label="Статусы">
              <div className="floor-legend-group">
                <div className="floor-legend-row" title="Включены и свободны — можно садить сразу">
                  <i className="floor-stat-dot" style={{ background: '#34d399' }} />
                  Свободно <strong>{floorCounts.free}</strong>
                </div>
                <div className="floor-legend-row" title="Выключены, сеанса нет — свободны; старт включит ПК (WOL)">
                  <i className="floor-stat-dot" style={{ background: '#64748b' }} />
                  Выкл <strong>{floorCounts.offline}</strong>
                </div>
                <div className="floor-legend-row" title="Идёт сеанс (в т.ч. если ПК временно без связи)">
                  <i className="floor-stat-dot" style={{ background: '#ef4444' }} />
                  Сеанс <strong>{floorCounts.busy}</strong>
                </div>
                <div className="floor-legend-row">
                  <i className="floor-stat-dot" style={{ background: '#a78bfa' }} />
                  Бронь <strong>{floorCounts.reserved}</strong>
                </div>
              </div>
            </aside>
            <span className="floor-toolbar-spacer" />
            <span className="muted" style={{ fontSize: 12 }}>
              Всего {floorCounts.total} ПК
            </span>
          </div>

          {floorView === 'grid' ? (
            <div className={`floor-hall${layoutEdit ? ' floor-hall--edit' : ''}`}>
              {layoutEdit && (
                <div className="floor-layout-toolbar">
                  <span className="muted" style={{ fontSize: 12 }}>
                    Перетащите ПК · угол плитки — изменить размер · позиции сохраняются в зоне
                  </span>
                </div>
              )}
              {floorZones.map((zone) => {
                let onlineFree = 0
                let offlineFree = 0
                for (const c of zone.pcs) {
                  const booking = bookingsByPc.get(c.id)
                  const bucket = classifyFloorPc(
                    c,
                    booking ? { startsAt: booking.startsAt, endsAt: booking.endsAt } : null,
                  )
                  if (bucket === 'onlineFree') onlineFree++
                  else if (bucket === 'offlineFree') offlineFree++
                }
                const amenity = isAmenityZone(zone.kind)
                const { cols, cells } = buildZoneSlots(zone.pcs, zone.gridColumns, zone.gridRows)
                const zoneAvail =
                  offlineFree > 0
                    ? `${onlineFree} своб. · ${offlineFree} выкл · ${zone.pcs.length} ПК`
                    : `${onlineFree} своб. · ${zone.pcs.length} ПК`
                return (
                  <section key={zone.id} className="floor-zone">
                    <div className="floor-zone-head">
                      <h3 style={{ color: zone.color ?? undefined }}>
                        {zone.name}
                        <span className="muted" style={{ marginLeft: 8, fontWeight: 500, fontSize: 13 }}>
                          {zoneKindLabel(zone.kind)}
                        </span>
                      </h3>
                      <span className="muted">
                        {amenity
                          ? 'служебная зона'
                          : `${zoneAvail} · ${cols}×${Math.ceil(cells.length / cols)}`}
                      </span>
                    </div>
                    {amenity ? (
                      <div
                        className="floor-amenity"
                        style={{ ['--zone-color' as string]: zone.color ?? '#ff6a00' }}
                      >
                        <strong>{zone.name}</strong>
                        <span>{zoneKindLabel(zone.kind)}</span>
                      </div>
                    ) : (
                      <div
                        className={`floor-pc-grid${layoutEdit ? ' floor-pc-grid--edit' : ''}`}
                        style={{ ['--floor-cols' as string]: cols }}
                      >
                        {cells.map((cell) => {
                          if (cell.covered) return null
                          const computer = cell.pc
                          if (!computer) {
                            return (
                              <div
                                key={`e-${cell.col}-${cell.row}`}
                                className="floor-pc-slot"
                                style={{
                                  gridColumn: cell.col + 1,
                                  gridRow: cell.row + 1,
                                }}
                                title={
                                  layoutEdit
                                    ? `Слот ${cell.col + 1}:${cell.row + 1} — бросьте ПК сюда`
                                    : `Слот ${cell.col + 1}:${cell.row + 1} — пусто`
                                }
                                onDragOver={
                                  layoutEdit
                                    ? (e) => {
                                        e.preventDefault()
                                        e.dataTransfer.dropEffect = 'move'
                                        e.currentTarget.classList.add('is-drop')
                                      }
                                    : undefined
                                }
                                onDragLeave={
                                  layoutEdit
                                    ? (e) => e.currentTarget.classList.remove('is-drop')
                                    : undefined
                                }
                                onDrop={
                                  layoutEdit
                                    ? (e) => {
                                        e.preventDefault()
                                        e.currentTarget.classList.remove('is-drop')
                                        const pcId = e.dataTransfer.getData('text/pc-id')
                                        if (pcId) onFloorPcDrop(pcId, cell.col, cell.row)
                                      }
                                    : undefined
                                }
                              />
                            )
                          }
                          const view = pcTileView(computer)
                          const pcBooking = bookingsByPc.get(computer.id)
                          const bottom = tileBottomText(view, {
                            remainingSeconds: computer.remainingSeconds,
                            bookingEndsAt: pcBooking?.endsAt,
                            bookingStartsAt: pcBooking?.startsAt,
                          })
                          const colSpan = Math.max(1, computer.gridColSpan ?? 1)
                          const rowSpan = Math.max(1, computer.gridRowSpan ?? 1)
                          return (
                            <FloorPcTileFace
                              key={computer.id}
                              name={computer.displayName ?? computer.windowsName}
                              view={view}
                              bottomText={bottom}
                              selected={selectedIds.includes(computer.id)}
                              className={`pc-tile${pcIsConsole(computer) ? ' floor-pc-tile--console' : ''}${layoutEdit ? ' fmap-pc--editable' : ''}`}
                              badge={pcIsConsole(computer) ? 'PS5' : null}
                              style={{
                                gridColumn: `${cell.col + 1} / span ${colSpan}`,
                                gridRow: `${cell.row + 1} / span ${rowSpan}`,
                              }}
                              draggable={layoutEdit}
                              onDragStart={
                                layoutEdit
                                  ? (e) => {
                                      e.dataTransfer.setData('text/pc-id', computer.id)
                                      e.dataTransfer.effectAllowed = 'move'
                                    }
                                  : undefined
                              }
                              onDragOver={
                                layoutEdit
                                  ? (e) => {
                                      e.preventDefault()
                                      e.dataTransfer.dropEffect = 'move'
                                    }
                                  : undefined
                              }
                              onDrop={
                                layoutEdit
                                  ? (e) => {
                                      e.preventDefault()
                                      e.stopPropagation()
                                      const pcId = e.dataTransfer.getData('text/pc-id')
                                      if (pcId && pcId !== computer.id) {
                                        onFloorPcDrop(pcId, cell.col, cell.row, {
                                          swapWithId: computer.id,
                                        })
                                      }
                                    }
                                  : undefined
                              }
                              onClick={(e) => {
                                if (skipClickRef.current) {
                                  skipClickRef.current = false
                                  return
                                }
                                selectComputer(computer.id, e.ctrlKey || e.metaKey)
                              }}
                            >
                              {layoutEdit ? (
                                <span
                                  className="fmap-el-resize fmap-el-resize--se"
                                  title="Размер ПК / PS5"
                                  onPointerDown={(e) => {
                                    e.preventDefault()
                                    e.stopPropagation()
                                    const startX = e.clientX
                                    const startY = e.clientY
                                    const startCols = colSpan
                                    const startRows = rowSpan
                                    const onUp = (ev: PointerEvent) => {
                                      window.removeEventListener('pointerup', onUp)
                                      const nextCols = Math.max(
                                        1,
                                        Math.min(8, startCols + Math.round((ev.clientX - startX) / 56)),
                                      )
                                      const nextRows = Math.max(
                                        1,
                                        Math.min(8, startRows + Math.round((ev.clientY - startY) / 56)),
                                      )
                                      if (nextCols !== startCols || nextRows !== startRows) {
                                        layoutMutation.mutate({
                                          id: computer.id,
                                          gridCol: cell.col,
                                          gridRow: cell.row,
                                          gridColSpan: nextCols,
                                          gridRowSpan: nextRows,
                                        })
                                      }
                                    }
                                    window.addEventListener('pointerup', onUp)
                                  }}
                                />
                              ) : null}
                            </FloorPcTileFace>
                          )
                        })}
                      </div>
                    )}
                  </section>
                )
              })}
              {floorZones.length === 0 && <p className="muted">Нет зон / ПК — настройте зоны и подтвердите ПК</p>}
            </div>
          ) : (
            <div className="floor-map-stage">
              <div className="floor-map floor-map--unified">
              {layoutEdit && (
                <div className="floor-layout-toolbar">
                  {LAYOUT_PALETTE.map((item) => (
                    <button
                      key={item.kind}
                      type="button"
                      className={`chip-btn${layoutTool === item.kind ? ' is-active' : ''}`}
                      onClick={() => {
                        setLayoutTool(item.kind)
                        setWallDraft(null)
                      }}
                    >
                      {item.label}
                    </button>
                  ))}
                  {wallDraft && <span className="muted">Кликните конец стены</span>}
                  <div className="floor-paint-settings">
                    <label>
                      Сетка
                      <input
                        type="number"
                        min={4}
                        max={48}
                        value={gridColsDraft}
                        onChange={(e) => setGridColsDraft(Number(e.target.value) || 4)}
                      />
                      ×
                      <input
                        type="number"
                        min={4}
                        max={36}
                        value={gridRowsDraft}
                        onChange={(e) => setGridRowsDraft(Number(e.target.value) || 4)}
                      />
                    </label>
                    <label>
                      Фон
                      <input
                        type="color"
                        value={bgDraft || '#0f172a'}
                        onChange={(e) => setBgDraft(e.target.value)}
                      />
                    </label>
                    <button
                      type="button"
                      className="chip-btn"
                      disabled={settingsMutation.isPending}
                      onClick={() =>
                        settingsMutation.mutate({
                          floorGridCols: gridColsDraft,
                          floorGridRows: gridRowsDraft,
                          floorBackgroundHex: bgDraft || null,
                        })
                      }
                    >
                      Сохранить фон/сетку
                    </button>
                  </div>
                  <span className="muted" style={{ fontSize: 12, width: '100%' }}>
                    Перетащите ПК · угол ПК/элемента — размер · клик инструментом — новый объект
                  </span>
                </div>
              )}
              {floorMapQuery.isError && (
                <p className="error">{(floorMapQuery.error as Error).message}</p>
              )}
              <FloorMapCanvas
                gridCols={floorMapQuery.data?.gridCols ?? 24}
                gridRows={floorMapQuery.data?.gridRows ?? 16}
                backgroundHex={floorMapQuery.data?.backgroundHex ?? bgDraft}
                computers={(floorMapQuery.data?.computers ?? approvedFloorPcs).map((c) => {
                  const fromList = computersQuery.data?.find((x) => x.id === c.id)
                  const pc = fromList ?? c
                  const booking = bookingsByPc.get(pc.id)
                  return {
                    ...pc,
                    sessionGuestName: pc.sessionGuestName,
                    bookingStartsAt: booking ? booking.startsAt : null,
                    bookingEndsAt: booking ? booking.endsAt : null,
                    bookingContactName: booking ? booking.contactName : null,
                    bookingContactPhone: booking ? booking.contactPhone : null,
                  }
                })}
                elements={floorMapQuery.data?.elements ?? []}
                mode={layoutEdit ? 'edit' : 'view'}
                hideInvisible={!layoutEdit}
                selectedPcId={selectedId}
                selectedPcIds={selectedIds}
                selectedElementId={layoutEdit ? selectedElementId : null}
                wallDraft={layoutEdit ? wallDraft : null}
                onPcClick={(pc, cell, additive) => {
                  if (skipClickRef.current) {
                    skipClickRef.current = false
                    return
                  }
                  if (layoutEdit && layoutTool !== 'select') {
                    onFloorCellClick(cell.col, cell.row)
                    return
                  }
                  setSelectedElementId(null)
                  selectComputer(pc.id, additive)
                }}
                onPcDrop={onFloorPcDrop}
                onPcResize={(pcId, colSpan, rowSpan) => {
                  layoutMutation.mutate({
                    id: pcId,
                    gridColSpan: colSpan,
                    gridRowSpan: rowSpan,
                  })
                }}
                onCellClick={onFloorCellClick}
                onElementClick={onFloorElementClick}
                onElementMove={onFloorElementMove}
                onElementResize={onFloorElementResize}
                onElementWallEndpoints={onFloorElementWallEndpoints}
                onElementLabelCommit={(el, label) => {
                  setElementLabelDraft(label)
                  saveElement(el, { label })
                }}
              />
              </div>
            </div>
          )}
        </section>

        <aside className="side-panel">
          {selected ? (
            <div className="side-panel-card floor-pc-panel">
              {(() => {
                const booking = selectedBooking
                    ? { startsAt: selectedBooking.startsAt, endsAt: selectedBooking.endsAt }
                    : null
                const live = activeSessionQuery.data
                const view = buildPcDisplay(
                  {
                    ...selected,
                    remainingSeconds: live?.remainingSeconds ?? selected.remainingSeconds,
                    sessionStatus: live?.status ?? selected.sessionStatus,
                    occupancyDetail:
                      isSessionStatus(live?.status, 'Paused')
                        ? 'Пауза'
                        : selected.occupancyDetail,
                  },
                  booking,
                )
                return (
                  <>
                    <header className="floor-pc-panel-head">
                      <div>
                        <h3>{selected.displayName ?? selected.windowsName}</h3>
                        {selected.zoneName && <p className="muted floor-pc-panel-zone">{selected.zoneName}</p>}
                      </div>
                      <span className="floor-pc-panel-badge" style={{ ['--pc-accent' as string]: view.color }}>
                        {view.statusLabel}
                      </span>
                    </header>
                    {pcIsConsole(selected) && (
                      <p className="muted" style={{ margin: '0 0 10px', fontSize: 12 }}>
                        PS5 · только учёт времени. Предупреждения на кассе — сотрудник сообщает гостю устно.
                      </p>
                    )}
                    <div className="pc-status-strip">
                      {view.timeText && (
                        <span
                          className={`pc-status-time${view.timeTone === 'busy-warn' ? ' is-warn' : ''}${
                            view.timeTone === 'booking' ? ' is-booking' : ''
                          }${view.timeTone === 'pause' ? ' is-pause' : ''}`}
                        >
                          {view.timeText}
                        </span>
                      )}
                      {selected.clientVersion && (
                        <span className="muted pc-status-meta">Shell v{selected.clientVersion}</span>
                      )}
                    </div>
                    <dl className="floor-pc-panel-meta">
                      {(selected.ipAddress || selected.macAddress) && (
                        <div>
                          <dt>Сеть</dt>
                          <dd>
                            {selected.ipAddress ?? '—'}
                            {selected.macAddress ? (
                              <span className="muted"> · {selected.macAddress}</span>
                            ) : null}
                          </dd>
                        </div>
                      )}
                      {selected.windowsName && selected.displayName && selected.windowsName !== selected.displayName && (
                        <div>
                          <dt>Хост</dt>
                          <dd>{selected.windowsName}</dd>
                        </div>
                      )}
                    </dl>
                  </>
                )
              })()}

              {!selected.isApproved ? (
                canManagePcs ? (
                <div className="pc-actions-primary">
                  <button
                    type="button"
                    onClick={() => {
                      setDisplayName(selected.windowsName)
                      setZoneId(zones[0]?.id ?? zoneId)
                      setApproveOpen(true)
                    }}
                  >
                    Подтвердить
                  </button>
                </div>
                ) : (
                  <p className="muted">ПК ожидает подтверждения владельцем/менеджером.</p>
                )
              ) : selected.currentSessionId ? (
                <div className="pc-actions-primary">
                  <p className="pc-guest-line">
                    <strong>{activeSessionQuery.data?.guestName ?? selected.sessionGuestName ?? 'Гость'}</strong>
                    {activeSessionQuery.data?.customerId ? (
                      <span className="muted"> · аккаунт</span>
                    ) : (
                      <span className="muted"> · гость</span>
                    )}
                    {(activeSessionQuery.data?.totalPrice ?? selected.sessionTotalPrice) != null && (
                      <> · {activeSessionQuery.data?.totalPrice ?? selected.sessionTotalPrice} ₸</>
                    )}
                  </p>
                  {selectedBooking && (
                    <div className="floor-booking-card floor-booking-card--inline">
                      <span className="floor-booking-card--inline-icon">📅</span>
                      <div>
                        <p style={{ margin: 0, fontWeight: 600, fontSize: 13 }}>
                          Бронь {selectedBooking.number}
                          <span className="muted"> · {bookingStatusLabel(selectedBooking.status)}</span>
                        </p>
                        <p style={{ margin: '2px 0 0', fontSize: 12 }}>
                          {selectedBooking.contactName} · {bookingTimeLabel(selectedBooking.startsAt)}–{bookingTimeLabel(selectedBooking.endsAt)}
                          {' · '}{formatDurationChip(selectedBooking.durationMinutes)}
                        </p>
                      </div>
                    </div>
                  )}
                  {isSessionStatus(activeSessionQuery.data?.status, 'Paused') ? (
                    <button type="button" onClick={() => resumeMutation.mutate()}>
                      Возобновить
                    </button>
                  ) : (
                    <button type="button" onClick={() => openExtend(30)}>
                      Продлить
                    </button>
                  )}
                  <div className="pc-actions-secondary action-chip-row">
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => setQuickBarOpen(true)}
                    >
                      Бар
                    </button>
                    {can(Perm.CustomersDeposit) && activeSessionQuery.data?.customerId && (
                      <button
                        type="button"
                        className="ghost"
                        onClick={() => setQuickCaseKeyOpen(true)}
                        title="Продать ключ SHIFT CASE этому гостю"
                      >
                        Ключ CASE
                      </button>
                    )}
                    {!isSessionStatus(activeSessionQuery.data?.status, 'Paused') &&
                      activeSessionQuery.data?.allowPause !== false && (
                        <button type="button" className="ghost" onClick={() => pauseMutation.mutate()}>
                          Пауза
                        </button>
                      )}
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => {
                        setTransferTargetId('')
                        setSessionError(null)
                        void queryClient.invalidateQueries({ queryKey: ['computers'] })
                        setTransferOpen(true)
                      }}
                    >
                      Перенести
                    </button>
                    <button type="button" className="danger ghost" onClick={() => setEndConfirm('end')}>
                      Завершить
                    </button>
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => {
                        setSessionError(null)
                        setTechCmdResult(null)
                        setTechOpen(true)
                      }}
                      disabled={pcIsConsole(selected)}
                      title={pcIsConsole(selected) ? 'Консоль без удалённого управления' : undefined}
                    >
                      Техника
                    </button>
                  </div>
                  {activeSessionQuery.data?.customerId ? (
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => setEndConfirm('save')}
                      disabled={(activeSessionQuery.data?.remainingSeconds ?? 0) < 60}
                    >
                      Сохранить минуты
                    </button>
                  ) : (
                    <div className="action-chip-row">
                      <button type="button" className="ghost" onClick={() => openAttach(false)}>
                        В аккаунт
                      </button>
                      <button
                        type="button"
                        className="ghost"
                        onClick={() => openAttach(true)}
                        disabled={(activeSessionQuery.data?.remainingSeconds ?? 0) < 60}
                      >
                        Сохранить в аккаунт
                      </button>
                    </div>
                  )}
                </div>
              ) : (
                <div className="pc-actions-primary">
                  {selectedBooking && (
                    <div className="floor-booking-card" style={{ marginBottom: 10 }}>
                      <p style={{ margin: '0 0 6px' }}>
                        <strong>Бронь {selectedBooking.number}</strong>
                        <span className="muted"> · {bookingStatusLabel(selectedBooking.status)}</span>
                      </p>
                      <p className="muted" style={{ margin: '0 0 4px' }}>
                        {selectedBooking.contactName} · {selectedBooking.contactPhone}
                      </p>
                      <p style={{ margin: '0 0 8px' }}>
                        {bookingTimeLabel(selectedBooking.startsAt)}
                        {'–'}
                        {bookingTimeLabel(selectedBooking.endsAt)}
                        {' · '}
                        {formatDurationChip(selectedBooking.durationMinutes)}
                      </p>
                      {(() => {
                        const minsTo = Math.round(
                          (new Date(selectedBooking.startsAt).getTime() - Date.now()) / 60_000,
                        )
                        if (minsTo > 30) {
                          return (
                            <p className="muted" style={{ margin: '0 0 8px', fontSize: 12 }}>
                              До прихода {minsTo} мин. Короткий чужой сеанс можно стартовать до soft-hold
                              (за 30 мин ПК закрепится).
                            </p>
                          )
                        }
                        if (minsTo > 0) {
                          return (
                            <p className="flash" style={{ margin: '0 0 8px', fontSize: 12 }}>
                              Soft-hold: ПК закреплён под эту бронь.
                            </p>
                          )
                        }
                        return (
                          <p className="flash" style={{ margin: '0 0 8px', fontSize: 12 }}>
                            Время брони наступило — запускайте сеанс по брони.
                          </p>
                        )
                      })()}
                      {selectedBooking.prepaidAmount > 0 && (
                        <p className="muted" style={{ margin: '0 0 8px' }}>
                          Предоплата {selectedBooking.prepaidAmount.toLocaleString('ru-RU')} ₸
                        </p>
                      )}
                      <div className="action-chip-row">
                        {canStartFromBooking && (
                          <button
                            type="button"
                            className="btn-booking-primary"
                            disabled={!selected?.isApproved || !!selected.currentSessionId}
                            onClick={() => requestOpenStart({ fromBooking: true })}
                          >
                            Старт по брони
                          </button>
                        )}
                        {isBookingStatus(selectedBooking.status, 'Pending', 'Confirmed') && (
                          <button
                            type="button"
                            className="ghost"
                            onClick={() =>
                              bookingActionMutation.mutate({ id: selectedBooking.id, action: 'arrived' })
                            }
                          >
                            Гость пришёл
                          </button>
                        )}
                        {isBookingStatus(
                          selectedBooking.status,
                          'Pending',
                          'Confirmed',
                          'Arrived',
                          'Active',
                        ) && (
                          <button
                            type="button"
                            className="ghost danger"
                            onClick={() => setCancelBookingId(selectedBooking.id)}
                          >
                            Отменить бронь
                          </button>
                        )}
                      </div>
                    </div>
                  )}
                  {!selectedBooking && (
                    <button
                      type="button"
                      disabled={!selected?.isApproved || !!selected.currentSessionId}
                      onClick={() => requestOpenStart()}
                    >
                      Старт сеанса
                    </button>
                  )}
                  {selectedBooking && (
                    <button
                      type="button"
                      className="ghost"
                      disabled={!selected?.isApproved || !!selected.currentSessionId}
                      onClick={() => requestOpenStart()}
                    >
                      Сеанс без брони…
                    </button>
                  )}
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => {
                      setSessionError(null)
                      setTechCmdResult(null)
                      setTechOpen(true)
                    }}
                    disabled={!selected || pcIsConsole(selected)}
                    title={selected && pcIsConsole(selected) ? 'Консоль без удалённого управления' : undefined}
                  >
                    Техника
                  </button>
                </div>
              )}

              {sessionError && <p className="error">{sessionError}</p>}
            </div>
          ) : (
            <div className="side-panel-card side-panel-card--empty">
              <p className="muted" style={{ margin: 0 }}>
                Выберите ПК на схеме
              </p>
            </div>
          )}

          {selectedElement && layoutEdit && (
            <div className="side-panel-card floor-el-panel">
              <h2 className="section-title" style={{ margin: 0 }}>
                {elementKindLabel(selectedElement.kind)}
              </h2>
              <label>
                Текст
                <textarea
                  rows={3}
                  maxLength={500}
                  value={elementLabelDraft}
                  onChange={(e) => setElementLabelDraft(e.target.value)}
                  onBlur={() => {
                    if (!selectedElement) return
                    if ((selectedElement.label ?? '') === elementLabelDraft) return
                    saveElement(selectedElement, { label: elementLabelDraft.slice(0, 500) })
                  }}
                  placeholder="Свободный текст…"
                />
              </label>
              <div className="floor-paint-row">
                <label>
                  Цвет
                  <input
                    type="color"
                    value={selectedElement.colorHex || defaultElementColor(selectedElement.kind)}
                    onChange={(e) => saveElement(selectedElement, { colorHex: e.target.value })}
                  />
                </label>
                <label>
                  Заливка
                  <input
                    type="color"
                    value={
                      selectedElement.fillHex ||
                      selectedElement.colorHex ||
                      defaultElementColor(selectedElement.kind)
                    }
                    onChange={(e) => saveElement(selectedElement, { fillHex: e.target.value })}
                  />
                </label>
              </div>
              <div className="floor-paint-row">
                <label>
                  Размер шрифта
                  <input
                    type="number"
                    min={8}
                    max={96}
                    value={selectedElement.fontSize ?? 14}
                    onChange={(e) =>
                      saveElement(selectedElement, {
                        fontSize: Math.min(96, Math.max(8, Number(e.target.value) || 14)),
                      })
                    }
                  />
                </label>
                <label>
                  Поворот °
                  <input
                    type="number"
                    min={0}
                    max={359}
                    value={selectedElement.rotationDeg ?? 0}
                    onChange={(e) =>
                      saveElement(selectedElement, {
                        rotationDeg: ((Number(e.target.value) || 0) % 360 + 360) % 360,
                      })
                    }
                  />
                </label>
              </div>
              <label className="floor-el-check">
                <input
                  type="checkbox"
                  checked={selectedElement.showIcon !== false}
                  onChange={(e) => saveElement(selectedElement, { showIcon: e.target.checked })}
                />
                Показать иконку
              </label>
              <div className="floor-paint-row">
                <label>
                  Col
                  <input
                    type="number"
                    min={0}
                    value={selectedElement.gridCol}
                    onChange={(e) =>
                      saveElement(selectedElement, {
                        gridCol: Math.max(0, Number(e.target.value) || 0),
                      })
                    }
                  />
                </label>
                <label>
                  Row
                  <input
                    type="number"
                    min={0}
                    value={selectedElement.gridRow}
                    onChange={(e) =>
                      saveElement(selectedElement, {
                        gridRow: Math.max(0, Number(e.target.value) || 0),
                      })
                    }
                  />
                </label>
              </div>
              {isWallSelected ? (
                <div className="floor-paint-row">
                  <label>
                    End Col
                    <input
                      type="number"
                      min={0}
                      value={selectedElement.endCol ?? selectedElement.gridCol}
                      onChange={(e) =>
                        saveElement(selectedElement, {
                          endCol: Math.max(0, Number(e.target.value) || 0),
                        })
                      }
                    />
                  </label>
                  <label>
                    End Row
                    <input
                      type="number"
                      min={0}
                      value={selectedElement.endRow ?? selectedElement.gridRow}
                      onChange={(e) =>
                        saveElement(selectedElement, {
                          endRow: Math.max(0, Number(e.target.value) || 0),
                        })
                      }
                    />
                  </label>
                </div>
              ) : (
                <div className="floor-paint-row">
                  <label>
                    Ширина
                    <input
                      type="number"
                      min={1}
                      max={48}
                      value={selectedElement.colSpan}
                      onChange={(e) =>
                        saveElement(selectedElement, {
                          colSpan: Math.max(1, Number(e.target.value) || 1),
                        })
                      }
                    />
                  </label>
                  <label>
                    Высота
                    <input
                      type="number"
                      min={1}
                      max={36}
                      value={selectedElement.rowSpan}
                      onChange={(e) =>
                        saveElement(selectedElement, {
                          rowSpan: Math.max(1, Number(e.target.value) || 1),
                        })
                      }
                    />
                  </label>
                </div>
              )}
              <div className="floor-paint-row">
                <label>
                  Толщина линии
                  <input
                    type="number"
                    min={1}
                    max={24}
                    value={selectedElement.strokeWidth ?? 4}
                    onChange={(e) =>
                      saveElement(selectedElement, {
                        strokeWidth: Math.min(24, Math.max(1, Number(e.target.value) || 4)),
                      })
                    }
                  />
                </label>
                <label>
                  Слой
                  <input type="number" readOnly value={selectedElement.sortOrder ?? 0} />
                </label>
              </div>
              <div className="action-chip-row">
                <button
                  type="button"
                  className="ghost"
                  onClick={() => nudgeElementLayer(selectedElement, 'back')}
                >
                  Назад
                </button>
                <button
                  type="button"
                  className="ghost"
                  onClick={() => nudgeElementLayer(selectedElement, 'front')}
                >
                  Вперёд
                </button>
              </div>
              <div className="action-chip-row">
                <button
                  type="button"
                  className="ghost"
                  onClick={() =>
                    saveElement(selectedElement, { isVisible: !selectedElement.isVisible })
                  }
                >
                  {selectedElement.isVisible ? 'Скрыть' : 'Показать'}
                </button>
                <button
                  type="button"
                  className="ghost"
                  onClick={() => deleteElementMutation.mutate(selectedElement.id)}
                >
                  Удалить
                </button>
              </div>
              <p className="muted" style={{ margin: 0, fontSize: 11 }}>
                Тяните синие маркеры по углам/краям — размер. Стены: линия или точки концов. Двойной
                клик — текст на карте.
              </p>
            </div>
          )}

          <div className="side-panel-card side-panel-card--upcoming">
            <FloorUpcomingFreePanel
              computers={approvedFloorPcs}
              freeCount={floorCounts.free}
              offlineCount={floorCounts.offline}
              busyCount={floorCounts.busy}
              selectedPcId={selectedId}
              onSelectPc={(id) => selectComputer(id, false)}
            />
          </div>
        </aside>
      </div>

      <ActionDialog
        open={approveOpen && !!selected && !selected.isApproved}
        onClose={() => !approveMutation.isPending && setApproveOpen(false)}
        title="Подтвердить ПК"
        description={`${selected?.windowsName ?? 'ПК'}${selected?.macAddress ? ` · MAC ${selected.macAddress}` : ''}`}
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setApproveOpen(false)} disabled={approveMutation.isPending}>
              Отмена
            </button>
            <button type="button" onClick={() => approveMutation.mutate()} disabled={approveMutation.isPending || !zoneId || !displayName.trim()}>
              Подтвердить
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Имя на карте
            <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
          </label>
          <label>
            Зона
            <select value={zoneId} onChange={(e) => setZoneId(e.target.value)}>
              <option value="">Выберите</option>
              {zones.map((z) => (
                <option key={z.id} value={z.id}>
                  {z.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      </ActionDialog>

      <ActionDialog
        open={
          startOpen &&
          (startSessionMutation.isPending ||
            (startFromBookingId
              ? !!selected?.isApproved && !selected.currentSessionId
              : startDialogTargets.length > 0))
        }
        onClose={() => {
          if (startSessionMutation.isPending) return
          setStartOpen(false)
          setStartFromBookingId(null)
        }}
        title={
          startFromBookingId
            ? 'Старт по брони'
            : startDialogTargets.length > 1
              ? `Старт на ${startDialogTargets.length} ПК`
              : 'Старт сеанса'
        }
        description={
          startFromBookingId && selectedBooking && selected
            ? `${selected.displayName ?? selected.windowsName} · ${selectedBooking.contactName} · бронь ${selectedBooking.number}`
            : startDialogTargets.length > 1
              ? 'Выберите тариф и оплату — сеанс запустится на всех выбранных ПК'
              : selected
                ? `${selected.displayName ?? selected.windowsName} · ${guestStatusLine(selected)}`
                : undefined
        }
        size="lg"
        footer={
          <>
            <button
              type="button"
              className="ghost"
              onClick={() => {
                setStartOpen(false)
                setStartFromBookingId(null)
                setStartBarCart([])
              }}
              disabled={startSessionMutation.isPending}
            >
              Отмена
            </button>
            <button
              type="button"
              disabled={
                !canStart ||
                startSessionMutation.isPending ||
                (!!startWalkInConflict?.blocked && !startFromBookingId) ||
                startCashShort ||
                startMixedShort ||
                (startPaymentRequired && !paymentMethod) ||
                (!!startCustomerId && startDialogTargets.length > 1 && !startFromBookingId)
              }
              onClick={() => startSessionMutation.mutate()}
            >
              {startSessionMutation.isPending
                ? wakeBeforeStart && startWakeTargets.length > 0
                  ? 'Включаем ПК…'
                  : 'Запуск…'
                : startGrandTotal > 0
                  ? `${wakeBeforeStart && startWakeTargets.length > 0 ? 'Включить и запустить' : 'Запустить'} · ${startGrandTotal.toLocaleString('ru-RU')} ₸`
                  : startDialogTargets.length > 1 && !startFromBookingId
                    ? `${wakeBeforeStart && startWakeTargets.length > 0 ? 'Включить и запустить' : 'Запустить'} · ${startDialogTargets.length}`
                    : wakeBeforeStart && startWakeTargets.length > 0
                      ? 'Включить и запустить'
                      : 'Запустить'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          {!startFromBookingId && startDialogTargets.length > 0 && (
            <div>
              <p className="section-kicker" style={{ marginBottom: 8 }}>
                Компьютеры ({startDialogTargets.length})
              </p>
              <div className="start-pc-chips">
                {startDialogTargets.map((c) => {
                  const offline = pcIsOfflineFree(c)
                  const canWake = pcCanWake(c)
                  return (
                    <div key={c.id} className={`start-pc-chip${offline ? ' is-offline' : ''}`}>
                      <div className="start-pc-chip__main">
                        <strong>{c.displayName ?? c.windowsName}</strong>
                        <span className="muted">
                          {c.zoneName ? `${c.zoneName} · ` : ''}
                          {offline ? (canWake ? 'офлайн · можно включить' : 'офлайн · нет MAC') : 'онлайн'}
                        </span>
                      </div>
                      {startDialogTargets.length > 1 && (
                        <button
                          type="button"
                          className="ghost start-pc-chip__remove"
                          title="Убрать из старта"
                          onClick={() => setSelectedIds((ids) => ids.filter((id) => id !== c.id))}
                        >
                          ×
                        </button>
                      )}
                    </div>
                  )
                })}
              </div>
              <p className="muted" style={{ margin: '8px 0 0', fontSize: 12 }}>
                На карте: Ctrl/⌘ + клик — выбрать несколько ПК, затем «Старт сеанса».
              </p>
            </div>
          )}

          {startOfflineTargets.length > 0 && (
            <label className="start-wake-check">
              <input
                type="checkbox"
                checked={wakeBeforeStart}
                onChange={(e) => setWakeBeforeStart(e.target.checked)}
                disabled={startWakeTargets.length === 0}
              />
              <span>
                {startWakeTargets.length > 1
                  ? `Включить ${startWakeTargets.length} офлайн ПК по LAN, затем старт`
                  : startWakeTargets.length === 1
                    ? `Включить «${startWakeTargets[0].displayName ?? startWakeTargets[0].windowsName}» по LAN, затем старт`
                    : 'Включить офлайн ПК по LAN перед стартом'}
                {startOfflineNoMac.length > 0 && (
                  <em>
                    {' '}
                    · без MAC: {startOfflineNoMac.map((c) => c.displayName ?? c.windowsName).join(', ')}
                  </em>
                )}
              </span>
            </label>
          )}

          {!startFromBookingId &&
            startDialogTargets.map((c) => bookingsByPc.get(c.id)).find(Boolean) && (
              <p className="muted" style={{ margin: 0, fontSize: 13 }}>
                Ближайшая бронь:{' '}
                {(() => {
                  const b =
                    startDialogTargets.map((c) => bookingsByPc.get(c.id)).find(Boolean) ?? null
                  if (!b) return null
                  return (
                    <>
                      <strong>{b.number}</strong> · {b.contactName} · {bookingRangeLabel(b)} (
                      {formatDurationChip(b.durationMinutes)})
                    </>
                  )
                })()}
              </p>
            )}
          {startWalkInConflict?.blocked && !startFromBookingId && (
            <p className="error" style={{ margin: 0 }}>
              {startWalkInConflict.message}
            </p>
          )}
          <div>
            <p className="section-kicker" style={{ marginBottom: 8 }}>
              Тариф
            </p>
            <div className="tariff-pick-grid">
              {availableTariffs.map((t) => {
                const active = t.id === tariffId
                const hint = tariffPromoHint(t)
                return (
                  <button
                    key={t.id}
                    type="button"
                    className={`tariff-pick${active ? ' is-active' : ''}`}
                    disabled={useTimeBank && canUseTimeBank}
                    onClick={() => setTariffId(t.id)}
                  >
                    <strong>{formatTariffTitle(t)}</strong>
                    <span>{formatTariffPrice(t)}</span>
                    {hint && <em>{hint}</em>}
                  </button>
                )
              })}
              {availableTariffs.length === 0 && (
                <p className="muted" style={{ margin: 0 }}>
                  Нет доступных тарифов для этой зоны
                </p>
              )}
            </div>
          </div>

          {startTariff && !(useTimeBank && canUseTimeBank) && (
            <>
              <div>
                <p className="section-kicker" style={{ marginBottom: 8 }}>
                  Сколько времени
                </p>
                <div className="order-actions" style={{ marginBottom: 10 }}>
                  {startDurationPresets(startTariff).map((m) => {
                    const price = quoteMinutes(startTariff, m, startLoyaltyPercent) * startPcCount
                    const active = startBillMinutes === m || durationMinutes === m
                    return (
                      <button
                        key={m}
                        type="button"
                        className={active ? undefined : 'ghost'}
                        onClick={() => {
                          setDurationMinutes(m)
                          setStartPaidAmount('')
                        }}
                      >
                        {formatDurationChip(m)}
                        {price > 0 ? ` · ${Math.round(price).toLocaleString('ru-RU')} ₸` : ''}
                      </button>
                    )
                  })}
                </div>

                {!isPackageTariff(startTariff) && (
                  <div className="field-grid">
                    <label>
                      Минут (вручную)
                      <input
                        type="number"
                        min={
                          isTimeWindowTariff(startTariff)
                            ? 1
                            : startTariff.minDurationMinutes && startTariff.minDurationMinutes > 0
                              ? startTariff.minDurationMinutes
                              : 1
                        }
                        max={
                          isTimeWindowTariff(startTariff) && startTariff.remainingMinutesInWindow
                            ? startTariff.remainingMinutesInWindow
                            : startTariff.maxDurationMinutes || undefined
                        }
                        step={5}
                        value={durationMinutes}
                        onChange={(e) => {
                          const raw = Number(e.target.value)
                          setDurationMinutes(Number.isFinite(raw) ? Math.max(0, raw) : 0)
                          setStartPaidAmount('')
                        }}
                        onBlur={() => {
                          const next = resolveEffectiveStartMinutes(startTariff, durationMinutes)
                          if (next > 0 && next !== durationMinutes) setDurationMinutes(next)
                        }}
                      />
                    </label>
                    <label>
                      На сумму, ₸
                      <input
                        type="number"
                        min={0}
                        step={50}
                        placeholder="например 1000 → минуты"
                        value={startPaidAmount}
                        onChange={(e) => {
                          const raw = e.target.value
                          setStartPaidAmount(raw)
                          const paid = Number(raw)
                          if (!Number.isFinite(paid) || paid <= 0) return
                          const mins = minutesFromPaidAmount(startTariff, paid, startLoyaltyPercent)
                          if (mins > 0) setDurationMinutes(mins)
                        }}
                      />
                    </label>
                  </div>
                )}

                {isPackageTariff(startTariff) && !isTimeWindowTariff(startTariff) && (
                  <p className="muted" style={{ margin: '0 0 8px', fontSize: 13 }}>
                    Пакет фиксированной длительности — {formatDurationChip(startBillMinutes || startTariff.fixedDurationMinutes || 0)}
                  </p>
                )}

                {startPaidAmount !== '' && Number(startPaidAmount) > 0 && (
                  <p
                    className={startPaidHintMins > 0 ? 'flash' : 'error'}
                    style={{ margin: '8px 0 0', fontSize: 13 }}
                  >
                    {startPaidHintMins > 0 ? (
                      <>
                        За {Number(startPaidAmount).toLocaleString('ru-RU')} ₸ хватает на{' '}
                        <strong>{formatDurationHuman(startPaidHintMins)}</strong>
                        {startDiscountBits.length > 0
                          ? ` · учтено: ${startDiscountBits.join(', ')}`
                          : ' · без скидок'}
                        {isTimeWindowTariff(startTariff) &&
                        startTariff.remainingMinutesInWindow &&
                        startPaidHintMins >= startTariff.remainingMinutesInWindow
                          ? ' · полное окно'
                          : ''}
                      </>
                    ) : (
                      <>
                        {Number(startPaidAmount).toLocaleString('ru-RU')} ₸ мало для этого тарифа
                        {startDiscountBits.length > 0
                          ? ` (даже со скидкой: ${startDiscountBits.join(', ')})`
                          : ''}
                      </>
                    )}
                  </p>
                )}
                {startBillMinutes > 0 &&
                  durationMinutes > 0 &&
                  startBillMinutes !== durationMinutes &&
                  !startPaidAmount && (
                    <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
                      По тарифу будет <strong>{formatDurationChip(startBillMinutes)}</strong>
                      {startTariff.minDurationMinutes
                        ? ` (мин. ${formatDurationChip(startTariff.minDurationMinutes)})`
                        : ''}
                    </p>
                  )}
              </div>

              <div className="start-summary-row">
                <div>
                  <span className="muted">Будет сеанс</span>
                  <strong>
                    {isTimeWindowTariff(startTariff) &&
                    startTariff.windowEndsAtLocal &&
                    startBillMinutes >= (startTariff.remainingMinutesInWindow ?? 0)
                      ? `до ${startTariff.windowEndsAtLocal}`
                      : formatDurationHuman(startBillMinutes || durationMinutes)}
                  </strong>
                </div>
                <div>
                  <span className="muted">По прайсу</span>
                  <strong>{startListExpected.toLocaleString('ru-RU')} ₸</strong>
                </div>
                {startDiscount > 0 && (
                  <div>
                    <span className="muted">Скидка</span>
                    <strong>
                      −{startDiscount.toLocaleString('ru-RU')} ₸
                      {startDiscountBits.length > 0 ? ` · ${startDiscountBits.join(' · ')}` : ''}
                    </strong>
                  </div>
                )}
                {startBookingPrepaid > 0 && (
                  <div>
                    <span className="muted">Предоплата брони</span>
                    <strong>−{Math.min(startBookingPrepaid, startExpected).toLocaleString('ru-RU')} ₸</strong>
                  </div>
                )}
                {startBarTotal > 0 && (
                  <div>
                    <span className="muted">Бар</span>
                    <strong>{startBarTotal.toLocaleString('ru-RU')} ₸</strong>
                  </div>
                )}
                <div className="start-summary-total">
                  <span className="muted">К оплате</span>
                  <strong>{startGrandTotal.toLocaleString('ru-RU')} ₸</strong>
                </div>
              </div>
            </>
          )}
          {(!startTariff || (useTimeBank && canUseTimeBank)) && startBarTotal > 0 && (
            <div className="start-summary-row">
              {useTimeBank && canUseTimeBank && (
                <div>
                  <span className="muted">Сеанс</span>
                  <strong>0 ₸ (банк)</strong>
                </div>
              )}
              <div>
                <span className="muted">Бар</span>
                <strong>{startBarTotal.toLocaleString('ru-RU')} ₸</strong>
              </div>
              <div className="start-summary-total">
                <span className="muted">Итого</span>
                <strong>{startGrandTotal.toLocaleString('ru-RU')} ₸</strong>
              </div>
            </div>
          )}
          {startTariff?.salePreview && !(useTimeBank && canUseTimeBank) && (
            <p className="muted" style={{ margin: 0 }}>
              {startTariff.salePreview}
            </p>
          )}
          {startCustomerId && startCustomer && (startCustomer.loyaltyLevelName || startLoyaltyPercent > 0) && (
            <p className="muted" style={{ margin: 0 }}>
              Лояльность: {startCustomer.loyaltyLevelName ?? 'уровень'}
              {startLoyaltyPercent > 0 ? ` (−${startLoyaltyPercent}% на время)` : ''}
            </p>
          )}

          {startFromBookingId && selectedBooking?.customerId && (
            <p className="muted" style={{ margin: 0 }}>
              Аккаунт брони:{' '}
              <strong>{selectedBooking.customerName ?? selectedBooking.contactName}</strong>
            </p>
          )}

          {!startFromBookingId && (
            <>
              <FloorCustomerPick
                query={startCustomerQuery}
                onQuery={(q) => {
                  setStartCustomerQuery(q)
                  setStartCustomerId('')
                  setStartCustomerPicked(null)
                  setUseTimeBank(false)
                }}
                picked={startCustomer}
                onPick={(c) => {
                  setStartCustomerId(c.id)
                  setStartCustomerQuery(c.fullName)
                  setStartCustomerPicked(c)
                }}
                onClear={() => {
                  setStartCustomerId('')
                  setStartCustomerQuery('')
                  setStartCustomerPicked(null)
                  setUseTimeBank(false)
                }}
                hits={startCustomersQuery.data ?? []}
                hitsLoading={startCustomersQuery.isFetching}
                zoneId={startPcZoneId}
                placeholder="Телефон, имя или логин — скидка и баланс"
              />
              {startCustomerQuery.trim().length >= 2 && !startCustomerId && (
                <p className="muted" style={{ margin: 0, fontSize: 12 }}>
                  Клиент не выбран — сеанс пойдёт как гость
                  {startCustomerQuery.trim() ? ` «${startCustomerQuery.trim()}»` : ''}.
                  Нажмите найденного человека или создайте аккаунт.
                </p>
              )}
              {startCustomerId && startDialogTargets.length > 1 && (
                <p className="error" style={{ margin: 0, fontSize: 13 }}>
                  Аккаунт нельзя повесить на компанию: клиент может играть только на одном ПК.
                  Уберите клиента или оставьте один компьютер.
                </p>
              )}
              {canUseTimeBank && (
                <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                  <input
                    type="checkbox"
                    checked={useTimeBank}
                    onChange={(e) => {
                      const on = e.target.checked
                      setUseTimeBank(on)
                      if (on) {
                        setDurationMinutes(Math.min(durationMinutes || startCustomerBank, startCustomerBank))
                      } else if (startTariff && isPackageTariff(startTariff) && startTariff.fixedDurationMinutes) {
                        setDurationMinutes(startTariff.fixedDurationMinutes)
                      } else if (startTariff && resolveTariffKind(startTariff) === 'Hourly') {
                        setDurationMinutes(startTariff.minDurationMinutes || 60)
                      }
                    }}
                  />
                  Списать банк этой зоны ({formatDurationChip(startCustomerBank)})
                </label>
              )}
              {useTimeBank && canUseTimeBank && (
                <label>
                  Минут с банка
                  <input
                    type="number"
                    min={1}
                    max={startCustomerBank || undefined}
                    value={durationMinutes}
                    onChange={(e) => setDurationMinutes(Number(e.target.value))}
                  />
                </label>
              )}
            </>
          )}

          {startPaymentRequired && (
            <FloorPaymentPick
              value={paymentMethod}
              onChange={setPaymentMethod}
              balanceAvailable={!!startCustomerId}
              balanceHint={
                startCustomer ? `${startCustomer.balance.toLocaleString('ru-RU')} ₸` : undefined
              }
            />
          )}

          {paymentMethod === 'Cash' && startCashDue > 0 && (
              <div className="field-grid">
                <label>
                  Дал клиент, ₸
                  <input
                    type="number"
                    min={0}
                    step={50}
                    value={startCashTendered}
                    onChange={(e) => setStartCashTendered(e.target.value)}
                    placeholder={String(Math.round(startCashDue))}
                  />
                </label>
                <div className="start-summary-row" style={{ margin: 0, alignContent: 'end' }}>
                  <div className="start-summary-total">
                    <span className="muted">Сдача</span>
                    <strong>
                      {startCashShort
                        ? 'мало'
                        : `${startCashChange.toLocaleString('ru-RU')} ₸`}
                    </strong>
                  </div>
                </div>
              </div>
            )}
          {paymentMethod === 'Mixed' && startAmountDue > 0 && (
            <div className="field-grid">
              <label>
                Наличные, ₸
                <input
                  type="number"
                  min={1}
                  step={50}
                  value={startMixedCash}
                  onChange={(e) => setStartMixedCash(e.target.value)}
                  placeholder={String(Math.round(startAmountDue / 2))}
                />
              </label>
              <div className="start-summary-row" style={{ margin: 0, alignContent: 'end' }}>
                <div className="start-summary-total">
                  <span className="muted">Kaspi</span>
                  <strong>
                    {Math.max(0, Math.round(startAmountDue - (Number(startMixedCash) || 0))).toLocaleString('ru-RU')} ₸
                  </strong>
                </div>
              </div>
              <p className="muted" style={{ margin: 0, gridColumn: '1 / -1', fontSize: 13 }}>
                К оплате {startAmountDue.toLocaleString('ru-RU')} ₸ · обе части должны быть больше 0
              </p>
            </div>
          )}
          {paymentMethod === 'Cash' &&
            startCashShort &&
            startCashTendered !== '' &&
            Number(startCashTendered) > 0 && (
              <p className="error" style={{ margin: 0, fontSize: 13 }}>
                Не хватает{' '}
                {(startCashDue - Number(startCashTendered)).toLocaleString('ru-RU')} ₸
                (к оплате {startCashDue.toLocaleString('ru-RU')} ₸)
              </p>
            )}
          {paymentMethod === 'Cash' && startCashChange > 0 && (
            <p className="flash" style={{ margin: 0 }}>
              Сдача: {startCashChange.toLocaleString('ru-RU')} ₸
            </p>
          )}

          {useTimeBank && canUseTimeBank && (
            <div className="start-summary-row">
              <div>
                <span className="muted">С банка</span>
                <strong>{formatDurationHuman(durationMinutes)}</strong>
              </div>
              <div>
                <span className="muted">К оплате</span>
                <strong>0 ₸</strong>
              </div>
            </div>
          )}

          <FloorStartBarCart
            cart={startBarCart}
            onChange={setStartBarCart}
            groupHint={
              startDialogTargets.length > 1
                ? `Бар одним чеком на компанию (${startDialogTargets.length} ПК), не на каждый компьютер.`
                : null
            }
          />

          {sessionError && <p className="error">{sessionError}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={extendOpen && extendTargets.length > 0}
        onClose={() => !extendMutation.isPending && setExtendOpen(false)}
        title={extendTargets.length > 1 ? `Продление · ${extendTargets.length} ПК` : 'Продление сеанса'}
        description={
          extendTargets.length > 1
            ? extendTargets
                .map((c) => c.displayName ?? c.windowsName)
                .slice(0, 6)
                .join(', ') + (extendTargets.length > 6 ? ` +${extendTargets.length - 6}` : '')
            : selected
              ? `${selected.displayName ?? selected.windowsName}${
                  selected.zoneName || activeSessionQuery.data?.zoneName
                    ? ` · ${selected.zoneName ?? activeSessionQuery.data?.zoneName}`
                    : ''
                } · ${activeSessionQuery.data?.tariffName ?? sessionTariff?.name ?? 'тариф'}`
              : undefined
        }
        size="lg"
        footer={
          <>
            <button
              type="button"
              className="ghost"
              onClick={() => setExtendOpen(false)}
              disabled={extendMutation.isPending}
            >
              Отмена
            </button>
            <button
              type="button"
              disabled={
                !extendCanExtend ||
                extendMutation.isPending ||
                extendCashShort ||
                extendMixedShort ||
                (extendPaymentRequired && !extendPay) ||
                (extendPay === 'Balance' && extendPcCount > 1)
              }
              onClick={() => extendMutation.mutate()}
            >
              {extendMutation.isPending
                ? 'Продлеваем…'
                : extendPay === 'Balance'
                  ? `Списать с баланса · ${extendExpected.toLocaleString('ru-RU')} ₸`
                  : extendExpected > 0
                    ? `Продлить${extendPcCount > 1 ? ` · ${extendPcCount}` : ''} · ${extendExpected.toLocaleString('ru-RU')} ₸`
                    : extendPcCount > 1
                      ? `Продлить · ${extendPcCount}`
                      : 'Продлить'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          {extendTargets.length > 1 && (
            <div className="start-pc-list">
              <p className="section-kicker" style={{ marginBottom: 8 }}>
                Компьютеры ({extendTargets.length})
              </p>
              <ul className="muted" style={{ margin: 0, paddingLeft: 18, fontSize: 13 }}>
                {extendTargets.map((c) => (
                  <li key={c.id}>{c.displayName ?? c.windowsName}</li>
                ))}
              </ul>
              <p className="muted" style={{ margin: '8px 0 0', fontSize: 12 }}>
                Одинаковое время и тариф на все · сумма × {extendTargets.length}
              </p>
            </div>
          )}
          <div>
            <p className="section-kicker" style={{ marginBottom: 8 }}>
              Тариф
            </p>
            <div className="tariff-pick-grid">
              {extendAvailableTariffs.map((t) => {
                const active = t.id === extendTariffId
                const hint = tariffPromoHint(t)
                return (
                  <button
                    key={t.id}
                    type="button"
                    className={`tariff-pick${active ? ' is-active' : ''}`}
                    onClick={() => pickExtendTariff(t)}
                  >
                    <strong>{formatTariffTitle(t)}</strong>
                    <span>{formatTariffPrice(t)}</span>
                    {hint && <em>{hint}</em>}
                  </button>
                )
              })}
              {extendAvailableTariffs.length === 0 && (
                <p className="muted" style={{ margin: 0 }}>
                  Нет доступных тарифов для этой зоны
                </p>
              )}
            </div>
          </div>

          {extendTariff && (
            <>
              <div>
                <p className="section-kicker" style={{ marginBottom: 8 }}>
                  Сколько времени
                </p>
                <div className="order-actions" style={{ marginBottom: 10 }}>
                  {startDurationPresets(extendTariff).map((m) => {
                    const price =
                      quoteMinutes(extendTariff, m, extendPcCount === 1 ? extendLoyaltyPercent : 0) *
                      extendPcCount
                    const active = extendBillMinutes === m || extendMinutes === m
                    return (
                      <button
                        key={m}
                        type="button"
                        className={active ? undefined : 'ghost'}
                        onClick={() => {
                          setExtendMinutes(m)
                          setExtendPaidAmount('')
                        }}
                      >
                        {formatDurationChip(m)}
                        {extendPcCount > 1 ? ` ×${extendPcCount}` : ''}
                        {price > 0 ? ` · ${Math.round(price).toLocaleString('ru-RU')} ₸` : ''}
                      </button>
                    )
                  })}
                </div>

                {!isPackageTariff(extendTariff) && (
                  <div className="field-grid">
                    <label>
                      Минут (вручную)
                      <input
                        type="number"
                        min={
                          isTimeWindowTariff(extendTariff)
                            ? 1
                            : extendTariff.minDurationMinutes && extendTariff.minDurationMinutes > 0
                              ? extendTariff.minDurationMinutes
                              : 1
                        }
                        max={
                          isTimeWindowTariff(extendTariff) && extendTariff.remainingMinutesInWindow
                            ? extendTariff.remainingMinutesInWindow
                            : extendTariff.maxDurationMinutes || undefined
                        }
                        step={5}
                        value={extendMinutes}
                        onChange={(e) => {
                          const raw = Number(e.target.value)
                          setExtendMinutes(Number.isFinite(raw) ? Math.max(0, raw) : 0)
                          setExtendPaidAmount('')
                        }}
                        onBlur={() => {
                          const next = resolveEffectiveStartMinutes(extendTariff, extendMinutes)
                          if (next > 0 && next !== extendMinutes) setExtendMinutes(next)
                        }}
                      />
                    </label>
                    <label>
                      На сумму, ₸
                      <input
                        type="number"
                        min={0}
                        step={50}
                        placeholder="например 1000 → минуты"
                        value={extendPaidAmount}
                        onChange={(e) => {
                          const raw = e.target.value
                          setExtendPaidAmount(raw)
                          const paid = Number(raw)
                          if (!Number.isFinite(paid) || paid <= 0) return
                          const mins = minutesFromPaidAmount(extendTariff, paid, extendLoyaltyPercent)
                          if (mins > 0) setExtendMinutes(mins)
                        }}
                      />
                    </label>
                  </div>
                )}

                {isPackageTariff(extendTariff) && !isTimeWindowTariff(extendTariff) && (
                  <p className="muted" style={{ margin: '0 0 8px', fontSize: 13 }}>
                    Пакет фиксированной длительности —{' '}
                    {formatDurationChip(extendBillMinutes || extendTariff.fixedDurationMinutes || 0)}
                  </p>
                )}

                {extendPaidAmount !== '' && Number(extendPaidAmount) > 0 && (
                  <p
                    className={extendPaidHintMins > 0 ? 'flash' : 'error'}
                    style={{ margin: '8px 0 0', fontSize: 13 }}
                  >
                    {extendPaidHintMins > 0 ? (
                      <>
                        За {Number(extendPaidAmount).toLocaleString('ru-RU')} ₸ хватает на{' '}
                        <strong>{formatDurationHuman(extendPaidHintMins)}</strong>
                        {extendDiscountBits.length > 0
                          ? ` · учтено: ${extendDiscountBits.join(', ')}`
                          : ' · без скидок'}
                        {isTimeWindowTariff(extendTariff) &&
                        extendTariff.remainingMinutesInWindow &&
                        extendPaidHintMins >= extendTariff.remainingMinutesInWindow
                          ? ' · полное окно'
                          : ''}
                      </>
                    ) : (
                      <>
                        {Number(extendPaidAmount).toLocaleString('ru-RU')} ₸ мало для этого тарифа
                        {extendDiscountBits.length > 0
                          ? ` (даже со скидкой: ${extendDiscountBits.join(', ')})`
                          : ''}
                      </>
                    )}
                  </p>
                )}
                {extendBillMinutes > 0 &&
                  extendMinutes > 0 &&
                  extendBillMinutes !== extendMinutes &&
                  !extendPaidAmount && (
                    <p className="muted" style={{ margin: '8px 0 0', fontSize: 13 }}>
                      По тарифу будет <strong>{formatDurationChip(extendBillMinutes)}</strong>
                      {extendTariff.minDurationMinutes
                        ? ` (мин. ${formatDurationChip(extendTariff.minDurationMinutes)})`
                        : ''}
                    </p>
                  )}
              </div>

              <div className="start-summary-row">
                <div>
                  <span className="muted">Добавится</span>
                  <strong>
                    {isTimeWindowTariff(extendTariff) &&
                    extendTariff.windowEndsAtLocal &&
                    extendBillMinutes >= (extendTariff.remainingMinutesInWindow ?? 0)
                      ? `до ${extendTariff.windowEndsAtLocal}`
                      : formatDurationHuman(extendBillMinutes || extendMinutes)}
                    {extendPcCount > 1 ? ` × ${extendPcCount}` : ''}
                  </strong>
                </div>
                <div>
                  <span className="muted">По прайсу</span>
                  <strong>{extendListExpected.toLocaleString('ru-RU')} ₸</strong>
                </div>
                {extendDiscount > 0 && (
                  <div>
                    <span className="muted">Скидка</span>
                    <strong>
                      −{extendDiscount.toLocaleString('ru-RU')} ₸
                      {extendDiscountBits.length > 0 ? ` · ${extendDiscountBits.join(' · ')}` : ''}
                    </strong>
                  </div>
                )}
                <div className="start-summary-total">
                  <span className="muted">К оплате{extendPcCount > 1 ? ` · ${extendPcCount} ПК` : ''}</span>
                  <strong>{extendExpected.toLocaleString('ru-RU')} ₸</strong>
                </div>
              </div>
            </>
          )}

          {extendTariff?.salePreview && (
            <p className="muted" style={{ margin: 0 }}>
              {extendTariff.salePreview}
            </p>
          )}
          {extendCustomer && (extendCustomer.loyaltyLevelName || extendLoyaltyPercent > 0) && (
            <p className="muted" style={{ margin: 0 }}>
              Лояльность: {extendCustomer.loyaltyLevelName ?? 'уровень'}
              {extendLoyaltyPercent > 0 ? ` (−${extendLoyaltyPercent}% на время)` : ''}
            </p>
          )}
          {activeSessionQuery.data?.guestName && !activeSessionQuery.data?.customerId && (
            <p className="muted" style={{ margin: 0 }}>
              Гость: <strong>{activeSessionQuery.data.guestName}</strong>
            </p>
          )}

          {extendPaymentRequired && (
            <FloorPaymentPick
              value={extendPay}
              onChange={(m) => {
                setExtendPay(m)
                if (m !== 'Cash') setExtendTendered('')
              }}
              balanceAvailable={extendPcCount === 1 && !!activeSessionQuery.data?.customerId}
              balanceHint={
                extendCustomer ? `${extendCustomer.balance.toLocaleString('ru-RU')} ₸` : undefined
              }
            />
          )}
          {extendPay === 'Mixed' && extendExpected > 0 && (
            <div className="field-grid">
              <label>
                Наличные, ₸
                <input
                  type="number"
                  min={1}
                  step={50}
                  value={extendMixedCash}
                  onChange={(e) => setExtendMixedCash(e.target.value)}
                  placeholder={String(Math.round(extendExpected / 2))}
                />
              </label>
              <div className="start-summary-row" style={{ margin: 0, alignContent: 'end' }}>
                <div className="start-summary-total">
                  <span className="muted">Kaspi</span>
                  <strong>
                    {Math.max(
                      0,
                      Math.round(extendExpected - (Number(extendMixedCash) || 0)),
                    ).toLocaleString('ru-RU')}{' '}
                    ₸
                  </strong>
                </div>
              </div>
              <p className="muted" style={{ margin: 0, gridColumn: '1 / -1', fontSize: 13 }}>
                К оплате {extendExpected.toLocaleString('ru-RU')} ₸
                {extendPcCount > 1 ? ` · ${extendPcCount} ПК` : ''} · обе части должны быть больше 0
              </p>
            </div>
          )}
          {extendPcCount > 1 && extendPay === 'Balance' && (
            <p className="error" style={{ margin: 0 }}>
              С баланса — только один ПК. Выберите наличные / карту / Kaspi.
            </p>
          )}

          {extendPay === 'Cash' && extendCashDue > 0 && (
            <div className="field-grid">
              <label>
                Дал клиент, ₸
                <input
                  type="number"
                  min={0}
                  step={50}
                  value={extendTendered}
                  onChange={(e) => setExtendTendered(e.target.value)}
                  placeholder={String(Math.round(extendCashDue))}
                />
              </label>
              <div className="start-summary-row" style={{ margin: 0, alignContent: 'end' }}>
                <div className="start-summary-total">
                  <span className="muted">Сдача</span>
                  <strong>
                    {extendCashShort ? 'мало' : `${extendCashChange.toLocaleString('ru-RU')} ₸`}
                  </strong>
                </div>
              </div>
            </div>
          )}
          {extendPay === 'Cash' &&
            extendCashShort &&
            extendTendered !== '' &&
            Number(extendTendered) > 0 && (
              <p className="error" style={{ margin: 0, fontSize: 13 }}>
                Не хватает {(extendCashDue - Number(extendTendered)).toLocaleString('ru-RU')} ₸ (к оплате{' '}
                {extendCashDue.toLocaleString('ru-RU')} ₸)
              </p>
            )}
          {extendPay === 'Cash' && extendChange > 0 && (
            <p className="flash" style={{ margin: 0 }}>
              Сдача: {extendChange.toLocaleString('ru-RU')} ₸
            </p>
          )}

          {sessionError && <p className="error">{sessionError}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={techOpen && techTargets.length > 0}
        onClose={() => setTechOpen(false)}
        title={techTargets.length > 1 ? `Техника · ${techTargets.length} ПК` : 'Техника'}
        description={
          techTargets.length === 1
            ? `${techTargets[0].displayName ?? techTargets[0].windowsName}${
                techTargets[0].zoneName ? ` · ${techTargets[0].zoneName}` : ''
              }`
            : techTargets.length > 1
              ? 'Команда применится ко всем выбранным ПК'
              : undefined
        }
        size="lg"
        footer={
          <button type="button" className="ghost" onClick={() => setTechOpen(false)}>
            Закрыть
          </button>
        }
      >
        {(() => {
          const multi = techTargets.length > 1
          const lockedCount = techTargets.filter(
            (c) => c.status === 'Locked' || c.status === 3,
          ).length
          const allLocked = lockedCount === techTargets.length
          const anyLocked = lockedCount > 0
          const busyCount = techTargets.filter((c) => !!c.currentSessionId).length
          const offlineCount = techTargets.filter((c) => buildPcDisplay(c).offlineBadge).length
          const wakeIds = techTargets
            .filter((c) => !!c.macAddress?.trim() && !c.currentSessionId)
            .map((c) => c.id)
          const canWake = wakeIds.length > 0
          const pending = commandMutation.isPending || wakeMutation.isPending || techCmdRunning
          const single = techTargets.length === 1 ? techTargets[0] : null

          function techLabel(verb: string) {
            if (!multi) return verb
            return `${verb} на ${techTargets.length} ПК`
          }

          return (
            <div className="tech-panel">
              {multi && (
                <div>
                  <p className="section-kicker" style={{ marginBottom: 8 }}>
                    Компьютеры ({techTargets.length})
                  </p>
                  <div className="start-pc-chips">
                    {techTargets.map((c) => {
                      const offline = buildPcDisplay(c).offlineBadge
                      const locked = c.status === 'Locked' || c.status === 3
                      return (
                        <div key={c.id} className={`start-pc-chip${offline ? ' is-offline' : ''}`}>
                          <div className="start-pc-chip__main">
                            <strong>{c.displayName ?? c.windowsName}</strong>
                            <span className="muted">
                              {[
                                c.zoneName,
                                offline ? 'офлайн' : 'онлайн',
                                locked ? 'блок' : null,
                                c.currentSessionId ? 'сеанс' : null,
                              ]
                                .filter(Boolean)
                                .join(' · ')}
                            </span>
                          </div>
                          {techTargets.length > 1 && (
                            <button
                              type="button"
                              className="ghost start-pc-chip__remove"
                              title="Убрать"
                              onClick={() => setSelectedIds((ids) => ids.filter((id) => id !== c.id))}
                            >
                              ×
                            </button>
                          )}
                        </div>
                      )
                    })}
                  </div>
                </div>
              )}

              <div className="tech-status" aria-label="Состояние ПК">
                {multi ? (
                  <>
                    <span className="tech-chip">Выбрано {techTargets.length}</span>
                    <span
                      className={`tech-chip${offlineCount > 0 ? ' tech-chip--warn' : ' tech-chip--ok'}`}
                    >
                      Офлайн {offlineCount}/{techTargets.length}
                    </span>
                    <span className={`tech-chip${anyLocked ? ' tech-chip--warn' : ''}`}>
                      Блок {lockedCount}/{techTargets.length}
                    </span>
                    <span className={`tech-chip${busyCount > 0 ? ' tech-chip--busy' : ''}`}>
                      Сеанс {busyCount}/{techTargets.length}
                    </span>
                    <span className={`tech-chip${canWake ? ' tech-chip--ok' : ' tech-chip--muted'}`}>
                      Можно включить {wakeIds.length}
                    </span>
                  </>
                ) : single ? (
                  <>
                    <span
                      className={`tech-chip${
                        buildPcDisplay(single).offlineBadge ? ' tech-chip--warn' : ' tech-chip--ok'
                      }`}
                    >
                      {buildPcDisplay(single).offlineBadge ? 'Офлайн' : 'Онлайн'}
                    </span>
                    <span className={`tech-chip${allLocked ? ' tech-chip--warn' : ''}`}>
                      {allLocked ? 'Экран заблокирован' : 'Экран открыт'}
                    </span>
                    <span className={`tech-chip${busyCount > 0 ? ' tech-chip--busy' : ''}`}>
                      {busyCount > 0 ? 'Сеанс идёт' : 'Без сеанса'}
                    </span>
                    {single.isMaintenance && (
                      <span className="tech-chip tech-chip--warn">Техработы</span>
                    )}
                    {(single.status === 'Updating' || single.status === 9) && (
                      <span className="tech-chip tech-chip--warn">Обновление</span>
                    )}
                    {single.clientVersion && (
                      <span className="tech-chip tech-chip--muted">Shell v{single.clientVersion}</span>
                    )}
                    {single.ipAddress && (
                      <span className="tech-chip tech-chip--muted">{single.ipAddress}</span>
                    )}
                    {single.macAddress ? (
                      <span className="tech-chip tech-chip--muted">MAC {single.macAddress}</span>
                    ) : (
                      <span className="tech-chip tech-chip--warn">Нет MAC</span>
                    )}
                  </>
                ) : null}
              </div>

              <section className="tech-group">
                <header className="tech-group-head">
                  <h3>Экран гостя</h3>
                  <p>
                    {multi
                      ? 'Блокировка и сообщение на все выбранные мониторы'
                      : 'Блокировка Shell и сообщение на монитор'}
                  </p>
                </header>
                <div className="tech-rows">
                  <button
                    type="button"
                    className="tech-row"
                    disabled={pending || allLocked}
                    onClick={() =>
                      setTechActionConfirm({
                        type: 'Lock',
                        label: techLabel('заблокировать экран'),
                      })
                    }
                  >
                    <span className="tech-row-main">
                      <strong>
                        {allLocked
                          ? multi
                            ? 'Все уже заблокированы'
                            : 'Уже заблокирован'
                          : multi
                            ? `Заблокировать экран (${techTargets.length - lockedCount})`
                            : 'Заблокировать экран'}
                      </strong>
                      <span>Shell поверх — гость не играет</span>
                    </span>
                  </button>
                  <button
                    type="button"
                    className="tech-row"
                    disabled={pending || !anyLocked}
                    onClick={() =>
                      setTechActionConfirm({
                        type: 'Unlock',
                        label: techLabel('снять блокировку экрана'),
                      })
                    }
                  >
                    <span className="tech-row-main">
                      <strong>
                        {multi && anyLocked
                          ? `Снять блокировку (${lockedCount})`
                          : 'Снять блокировку'}
                      </strong>
                      <span>Вернуть экран гостю</span>
                    </span>
                  </button>
                  <button
                    type="button"
                    className="tech-row"
                    disabled={pending}
                    onClick={() => {
                      setTechMessage('')
                      setMessageOpen(true)
                    }}
                  >
                    <span className="tech-row-main">
                      <strong>
                        {multi ? `Сообщение гостям (${techTargets.length})` : 'Сообщение гостю'}
                      </strong>
                      <span>
                        {multi
                          ? 'Один текст на все выбранные мониторы'
                          : 'Текст всплывёт на мониторе ПК'}
                      </span>
                    </span>
                  </button>
                </div>
              </section>

              <section className="tech-group">
                <header className="tech-group-head">
                  <h3>Клиент SHIFT</h3>
                  <p>Процессы Windows и обновление Shell</p>
                </header>
                <div className="tech-rows">
                  <button
                    type="button"
                    className="tech-row"
                    disabled={pending || multi}
                    title={multi ? 'Диспетчер задач — только для одного ПК' : undefined}
                    onClick={() => {
                      if (multi) return
                      setTechOpen(false)
                      setTaskMgrOpen(true)
                      setTaskMgrError(null)
                      void refreshTaskManager(true)
                    }}
                  >
                    <span className="tech-row-main">
                      <strong>Диспетчер задач</strong>
                      <span>
                        {multi
                          ? 'Сначала оставьте один ПК в выборе'
                          : 'Список процессов · можно завершить'}
                      </span>
                    </span>
                    {!multi && (
                      <span className="tech-row-go" aria-hidden>
                        →
                      </span>
                    )}
                  </button>
                  <button
                    type="button"
                    className="tech-row"
                    disabled={pending}
                    onClick={() =>
                      setTechActionConfirm({
                        type: 'UpdateClient',
                        label: techLabel('обновить клиент SHIFT'),
                      })
                    }
                  >
                    <span className="tech-row-main">
                      <strong>
                        {multi
                          ? `Обновить клиент (${techTargets.length})`
                          : 'Обновить клиент'}
                      </strong>
                      <span>Скачать и поставить свежий Shell</span>
                    </span>
                  </button>
                </div>
              </section>

              <section className="tech-group">
                <header className="tech-group-head">
                  <h3>Командная строка</h3>
                  <p>
                    {multi
                      ? `cmd.exe на ${techTargets.length} ПК · вывод только при одном ПК`
                      : 'Выполнить команду через cmd.exe на этом ПК'}
                  </p>
                </header>
                <div className="tech-cmd">
                  <label className="tech-cmd-label">
                    Команда
                    <input
                      type="text"
                      className="tech-cmd-input"
                      value={techCmdLine}
                      disabled={pending || techCmdRunning}
                      placeholder='например: ipconfig /all'
                      spellCheck={false}
                      autoComplete="off"
                      onChange={(e) => setTechCmdLine(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key !== 'Enter' || e.shiftKey) return
                        e.preventDefault()
                        const line = techCmdLine.trim()
                        if (!line || pending || techCmdRunning) return
                        setTechActionConfirm({
                          type: 'RunCmd',
                          label: multi
                            ? `выполнить CMD на ${techTargets.length} ПК`
                            : 'выполнить CMD на ПК',
                          payloadJson: JSON.stringify({ command: line }),
                        })
                      }}
                    />
                  </label>
                  <button
                    type="button"
                    className="tech-row tech-row--warn"
                    disabled={pending || techCmdRunning || !techCmdLine.trim()}
                    onClick={() => {
                      const line = techCmdLine.trim()
                      if (!line) return
                      setTechActionConfirm({
                        type: 'RunCmd',
                        label: multi
                          ? `выполнить CMD на ${techTargets.length} ПК`
                          : 'выполнить CMD на ПК',
                        payloadJson: JSON.stringify({ command: line }),
                      })
                    }}
                  >
                    <span className="tech-row-main">
                      <strong>
                        {techCmdRunning
                          ? 'Выполняется…'
                          : multi
                            ? `Выполнить на ${techTargets.length} ПК`
                            : 'Выполнить'}
                      </strong>
                      <span>
                        {multi
                          ? 'Массовая отправка · нужен Shell 0.7.4+'
                          : 'До 90 сек · stdout/stderr вернутся сюда'}
                      </span>
                    </span>
                  </button>
                  {techCmdResult && (
                    <pre className="tech-cmd-out" tabIndex={0}>
                      {techCmdResult}
                    </pre>
                  )}
                </div>
              </section>

              <section className="tech-group tech-group--power">
                <header className="tech-group-head">
                  <h3>Питание</h3>
                  <p>Включение по LAN · перезагрузка и выключение через Shell</p>
                </header>
                <div className="tech-rows">
                  <button
                    type="button"
                    className="tech-row"
                    disabled={pending || !canWake}
                    title={
                      !canWake
                        ? 'Нужен MAC и ПК без активного сеанса'
                        : `Wake-on-LAN на ${wakeIds.length} ПК`
                    }
                    onClick={() =>
                      setTechActionConfirm({
                        type: 'Wake',
                        label:
                          wakeIds.length > 1
                            ? `включить ${wakeIds.length} ПК (Wake-on-LAN)`
                            : 'включить ПК (Wake-on-LAN)',
                      })
                    }
                  >
                    <span className="tech-row-main">
                      <strong>
                        {canWake
                          ? wakeIds.length > 1
                            ? `Включить ПК (${wakeIds.length})`
                            : 'Включить ПК'
                          : 'Включить недоступно'}
                      </strong>
                      <span>
                        {!canWake
                          ? 'Нужен MAC, без активного сеанса'
                          : 'Wake-on-LAN по MAC-адресу'}
                      </span>
                    </span>
                  </button>
                  <button
                    type="button"
                    className="tech-row tech-row--warn"
                    disabled={pending}
                    onClick={() =>
                      setTechActionConfirm({
                        type: 'Restart',
                        label: techLabel('перезагрузить'),
                      })
                    }
                  >
                    <span className="tech-row-main">
                      <strong>
                        {multi ? `Перезагрузить (${techTargets.length})` : 'Перезагрузить'}
                      </strong>
                      <span>
                        {multi
                          ? 'Сеансы на выбранных ПК прервутся'
                          : 'Сеанс на этом ПК прервётся'}
                      </span>
                    </span>
                  </button>
                  <button
                    type="button"
                    className="tech-row tech-row--danger"
                    disabled={pending}
                    onClick={() =>
                      setTechActionConfirm({
                        type: 'Shutdown',
                        label: techLabel('выключить'),
                      })
                    }
                  >
                    <span className="tech-row-main">
                      <strong>
                        {multi ? `Выключить (${techTargets.length})` : 'Выключить'}
                      </strong>
                      <span>Полное выключение ПК</span>
                    </span>
                  </button>
                </div>
              </section>

              {pending && (
                <p className="muted tech-pending">
                  {techCmdRunning
                    ? 'Выполнение CMD…'
                    : wakeMutation.isPending
                      ? 'Отправка Wake-on-LAN…'
                      : multi
                        ? `Отправка команды на ${techTargets.length} ПК…`
                        : 'Отправка команды…'}
                </p>
              )}
              {sessionError && <p className="error">{sessionError}</p>}
            </div>
          )
        })()}
      </ActionDialog>

      <ActionDialog
        open={taskMgrOpen && !!selected?.isApproved}
        onClose={() => !taskMgrLoading && setTaskMgrOpen(false)}
        title="Диспетчер задач"
        description={selected?.displayName ?? selected?.windowsName}
        size="lg"
        footer={
          <>
            <button
              type="button"
              className="ghost"
              disabled={taskMgrLoading}
              onClick={() => void refreshTaskManager(false)}
            >
              Обновить список
            </button>
            <button
              type="button"
              className="ghost"
              disabled={taskMgrLoading}
              onClick={() => void refreshTaskManager(true)}
            >
              Открыть на ПК
            </button>
            <button type="button" className="ghost" onClick={() => setTaskMgrOpen(false)} disabled={taskMgrLoading}>
              Закрыть
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="page-toolbar" style={{ margin: 0 }}>
            <input
              placeholder="Поиск процесса…"
              value={taskMgrFilter}
              onChange={(e) => setTaskMgrFilter(e.target.value)}
              style={{ minWidth: 220, flex: 1 }}
            />
            {taskMgrCapturedAt && (
              <span className="muted" style={{ whiteSpace: 'nowrap' }}>
                {taskMgrLoading ? 'Загрузка…' : `${filteredTaskMgr.length} · ${taskMgrCapturedAt}`}
              </span>
            )}
          </div>
          {taskMgrError && <p className="error">{taskMgrError}</p>}
          <div className="taskmgr-table-wrap">
            <table className="taskmgr-table">
              <thead>
                <tr>
                  <th>Имя</th>
                  <th>PID</th>
                  <th>МБ</th>
                  <th>Окно</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {filteredTaskMgr.map((p) => (
                  <tr key={`${p.pid}-${p.name}`}>
                    <td>
                      <strong>{p.name}</strong>
                    </td>
                    <td className="muted">{p.pid}</td>
                    <td>{p.memoryMb}</td>
                    <td className="muted taskmgr-title">{p.windowTitle || '—'}</td>
                    <td>
                      {p.canKill ? (
                        <button
                          type="button"
                          className="ghost danger-text"
                          disabled={taskMgrLoading}
                          onClick={() => void killRemoteProcess(p)}
                        >
                          Завершить
                        </button>
                      ) : (
                        <span className="muted">защита</span>
                      )}
                    </td>
                  </tr>
                ))}
                {!taskMgrLoading && filteredTaskMgr.length === 0 && (
                  <tr>
                    <td colSpan={5} className="muted">
                      Нет данных — нажмите «Обновить список»
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </ActionDialog>

      <ActionDialog
        open={messageOpen && techTargets.length > 0}
        onClose={() => !commandMutation.isPending && setMessageOpen(false)}
        title={techTargets.length > 1 ? `Сообщение на ${techTargets.length} ПК` : 'Сообщение на ПК'}
        description={
          techTargets.length === 1
            ? (techTargets[0].displayName ?? techTargets[0].windowsName)
            : techTargets
                .slice(0, 4)
                .map((c) => c.displayName ?? c.windowsName)
                .join(', ') + (techTargets.length > 4 ? '…' : '')
        }
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setMessageOpen(false)} disabled={commandMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={commandMutation.isPending || !techMessage.trim()}
              onClick={() =>
                commandMutation.mutate({
                  type: 'ShowMessage',
                  payloadJson: JSON.stringify({ text: techMessage.trim() }),
                  computerIds: techTargets.map((c) => c.id),
                })
              }
            >
              {techTargets.length > 1 ? `Отправить · ${techTargets.length}` : 'Отправить'}
            </button>
          </>
        }
      >
        <label>
          Текст
          <textarea value={techMessage} onChange={(e) => setTechMessage(e.target.value)} rows={3} />
        </label>
      </ActionDialog>

      <ActionDialog
        open={transferOpen && !!selected?.currentSessionId}
        onClose={() => !transferMutation.isPending && setTransferOpen(false)}
        title="Перенос сеанса"
        description={
          selected
            ? `${selected.displayName ?? selected.windowsName}${selected.zoneName ? ` · зона ${selected.zoneName}` : ''}`
            : undefined
        }
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setTransferOpen(false)} disabled={transferMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={transferMutation.isPending || !transferTargetId}
              onClick={() => transferMutation.mutate()}
            >
              Перенести
            </button>
          </>
        }
      >
        <div className="form-stack">
          <p className="muted" style={{ margin: 0 }}>
            Свободные ПК той же зоны
            {selected?.zoneName ? ` «${selected.zoneName}»` : ''}. Выключенные ПК будут включены автоматически (WOL).
          </p>
          <label>
            Целевой ПК
            <select value={transferTargetId} onChange={(e) => setTransferTargetId(e.target.value)}>
              <option value="">— выбрать —</option>
              {freeComputers.map((c) => {
                const offline = resolveOccupancy(c).kind === 'Offline'
                return (
                  <option key={c.id} value={c.id}>
                    {c.displayName ?? c.windowsName}
                    {c.zoneName ? ` · ${c.zoneName}` : ''}
                    {offline ? ' · ⚡ выключен' : ''}
                  </option>
                )
              })}
            </select>
          </label>
          {freeComputers.length === 0 && (
            <p className="muted">
              Нет свободных ПК в этой зоне
              {selected?.zoneName ? ` (${selected.zoneName})` : ''}
            </p>
          )}
          {sessionError && <p className="error">{sessionError}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={!!bookingConflict}
        onClose={() => setBookingConflict(null)}
        title="На ПК есть бронь"
        description={
          bookingConflict
            ? `${bookingConflict.number} · ${bookingConflict.contactName} · ${bookingRangeLabel(bookingConflict)}`
            : undefined
        }
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setBookingConflict(null)}>
              Закрыть
            </button>
          </>
        }
      >
        <div className="form-stack">
          {bookingConflict && (
            <p className="error" style={{ marginTop: 0 }}>
              {explainBookingBlock(bookingConflict, durationMinutes).message ??
                'ПК занят бронью. Выберите действие:'}
            </p>
          )}
          <p className="muted" style={{ marginTop: 0 }}>
            Выберите действие:
          </p>
          <button
            type="button"
            disabled={
              !bookingConflict ||
              !isOpenBookingStatus(bookingConflict.status) ||
              !bookingConflict.computers.some((c) => c.computerId === selectedId && !c.gamingSessionId)
            }
            onClick={() => {
              const b = bookingConflict
              setBookingConflict(null)
              if (b) requestOpenStart({ fromBooking: true })
            }}
          >
            Старт по брони
          </button>
          <button
            type="button"
            className="ghost"
            disabled={!bookingConflict || cancelBookingThenStartMutation.isPending}
            onClick={() => bookingConflict && cancelBookingThenStartMutation.mutate(bookingConflict.id)}
          >
            Отменить бронь и стартовать новый сеанс
          </button>
          <button
            type="button"
            className="ghost"
            disabled={!bookingConflict || cancelBookingOnlyMutation.isPending}
            onClick={() => bookingConflict && cancelBookingOnlyMutation.mutate(bookingConflict.id)}
          >
            Только отменить бронь
          </button>
          {sessionError && <p className="error">{sessionError}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={bookOpen && selectedComputers.some((c) => c.isApproved)}
        onClose={() => !bookMutation.isPending && setBookOpen(false)}
        title="Бронь с карты"
        description={`ПК: ${selectedComputers
          .filter((c) => c.isApproved)
          .map((c) => c.displayName ?? c.windowsName)
          .join(', ')}`}
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setBookOpen(false)} disabled={bookMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={bookMutation.isPending || !bookContactName.trim() || !bookContactPhone.trim()}
              onClick={() => bookMutation.mutate()}
            >
              Создать бронь
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="field-grid">
            <label>
              Имя
              <input value={bookContactName} onChange={(e) => setBookContactName(e.target.value)} />
            </label>
            <label>
              Телефон
              <input value={bookContactPhone} onChange={(e) => setBookContactPhone(e.target.value)} />
            </label>
          </div>
          <div className="field-grid">
            <label>
              Дата
              <input type="date" value={bookDate} onChange={(e) => setBookDate(e.target.value)} />
            </label>
            <label>
              Когда придут
              <input type="time" value={bookTime} onChange={(e) => setBookTime(e.target.value)} />
            </label>
            <label>
              На сколько (часов)
              <input
                type="number"
                min={0.5}
                step={0.5}
                value={bookDuration / 60}
                onChange={(e) => {
                  const hours = Number(e.target.value)
                  setBookDuration(Math.max(15, Math.round(hours * 60)))
                }}
              />
            </label>
          </div>
          <div className="order-actions">
            {[1, 2, 3, 5].map((h) => (
              <button
                key={h}
                type="button"
                className={bookDuration === h * 60 ? undefined : 'ghost'}
                onClick={() => setBookDuration(h * 60)}
              >
                {h} ч
              </button>
            ))}
          </div>
          <p className="muted" style={{ margin: 0, fontSize: 12 }}>
            Слот: {bookTime || '—'} · {formatDurationChip(bookDuration)}
            {bookTime
              ? (() => {
                  const [hh, mm] = bookTime.split(':').map(Number)
                  if (Number.isNaN(hh)) return null
                  const end = new Date()
                  end.setHours(hh, mm || 0, 0, 0)
                  end.setMinutes(end.getMinutes() + bookDuration)
                  return ` → до ${end.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })}`
                })()
              : null}
            . ПК блокируется только за 30 мин до начала.
          </p>
          <div className="field-grid">
            <label>
              Предоплата ₸
              <input
                type="number"
                min={0}
                step={100}
                value={bookPrepaid}
                onChange={(e) => setBookPrepaid(Number(e.target.value))}
              />
            </label>
            {bookPrepaid > 0 && (
              <label>
                Способ
                <select
                  value={bookPrepayMethod}
                  onChange={(e) => setBookPrepayMethod(e.target.value as typeof bookPrepayMethod)}
                >
                  <option value="Cash">Наличные</option>
                  <option value="KaspiQr">Kaspi QR</option>
                </select>
              </label>
            )}
          </div>
          <label>
            Комментарий
            <input value={bookComment} onChange={(e) => setBookComment(e.target.value)} />
          </label>
          <p className="muted" style={{ margin: 0, fontSize: 12 }}>
            Время — локальное филиала (Asia/Almaty).
          </p>
          {sessionError && <p className="error">{sessionError}</p>}
        </div>
      </ActionDialog>

      <FloorOrdersDialog
        open={ordersOpen}
        onClose={() => {
          setOrdersOpen(false)
          setOrdersHighlightId(null)
          setOrdersFilterPcId(null)
        }}
        highlightOrderId={ordersHighlightId}
        computerId={ordersFilterPcId}
      />

      <ActionDialog
        open={pendingPcsOpen}
        onClose={() => setPendingPcsOpen(false)}
        title="Новые ПК"
        description="Ожидают подтверждения после регистрации"
        size="md"
        footer={
          <button type="button" className="ghost" onClick={() => setPendingPcsOpen(false)}>
            Закрыть
          </button>
        }
      >
        <ul className="pending-list" style={{ margin: 0 }}>
          {pendingPcs.map((computer) => (
            <li key={computer.id}>
              <button
                type="button"
                className="ghost full"
                onClick={() => {
                  setPendingPcsOpen(false)
                  selectComputer(computer.id, false)
                  setDisplayName(computer.windowsName)
                  setZoneId(zones[0]?.id ?? '')
                  setApproveOpen(true)
                }}
              >
                <strong>{computer.windowsName}</strong>
                <span className="muted">MAC {computer.macAddress ?? '—'}</span>
              </button>
            </li>
          ))}
          {pendingPcs.length === 0 && <li className="muted">Нет ожидающих ПК</li>}
        </ul>
      </ActionDialog>

      {selected?.currentSessionId ? (
        <FloorQuickBarDialog
          open={quickBarOpen}
          onClose={() => setQuickBarOpen(false)}
          computerId={selected.id}
          gamingSessionId={selected.currentSessionId}
          customerId={activeSessionQuery.data?.customerId}
          computerName={selected.displayName ?? selected.windowsName}
          onSold={(message) => showFloorResult('Продажа бара выполнена', message)}
        />
      ) : (
        <FloorQuickBarDialog
          open={quickBarOpen}
          onClose={() => setQuickBarOpen(false)}
          title="Быстрая продажа бара"
          onSold={(message) => showFloorResult('Продажа бара выполнена', message)}
        />
      )}

      <FloorQuickCaseKeyDialog
        open={quickCaseKeyOpen}
        onClose={() => setQuickCaseKeyOpen(false)}
        presetCustomerId={
          selected?.currentSessionId ? activeSessionQuery.data?.customerId : null
        }
        presetCustomerName={
          selected?.currentSessionId
            ? (activeSessionQuery.data?.guestName ?? selected.sessionGuestName)
            : null
        }
        onSold={(message) => showFloorResult('Ключ CASE продан', message)}
      />

      <ActionDialog
        open={!!floorResult}
        onClose={() => setFloorResult(null)}
        title={floorResult?.title ?? 'Результат'}
        size="sm"
        footer={
          <button type="button" onClick={() => setFloorResult(null)}>
            Понятно
          </button>
        }
      >
        <div className={`floor-result-box${floorResult?.tone === 'error' ? ' is-error' : floorResult?.tone === 'warning' ? ' is-warning' : ''}`}>
          <p className="floor-result-box__title">
            {floorResult?.tone === 'error'
              ? 'Не выполнено'
              : floorResult?.tone === 'warning'
                ? 'Частично'
                : 'Готово'}
          </p>
          <p className="floor-result-box__message">{floorResult?.message}</p>
        </div>
      </ActionDialog>

      <ConfirmDialog
        open={!!cancelBookingId}
        onClose={() => setCancelBookingId(null)}
        title="Отменить бронь?"
        message="Бронь будет отменена. Предоплата вернётся по правилам клуба."
        confirmLabel="Отменить бронь"
        danger
        loading={cancelBookingOnlyMutation.isPending}
        onConfirm={() => {
          if (!cancelBookingId) return
          cancelBookingOnlyMutation.mutate(cancelBookingId, {
            onSettled: () => setCancelBookingId(null),
          })
        }}
      />
      <ActionDialog
        open={attachOpen}
        onClose={() => {
          if (attachMutation.isPending) return
          setAttachOpen(false)
        }}
        title={attachThenSave ? 'Сохранить сеанс в аккаунт' : 'Привязать аккаунт'}
        description={
          attachThenSave
            ? 'Выберите или создайте клиента — сеанс закроется, минуты уйдут в банк зоны.'
            : 'Гостевой сеанс продолжается, просто привязывается к аккаунту.'
        }
        size="md"
        footer={
          <>
            <button
              type="button"
              className="ghost"
              onClick={() => setAttachOpen(false)}
              disabled={attachMutation.isPending}
            >
              Отмена
            </button>
            <button
              type="button"
              disabled={!attachPicked || attachMutation.isPending}
              onClick={() => attachMutation.mutate()}
            >
              {attachMutation.isPending
                ? 'Сохраняем…'
                : attachThenSave
                  ? 'Привязать и сохранить минуты'
                  : 'Привязать'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <FloorCustomerPick
            query={attachQuery}
            onQuery={(q) => {
              setAttachQuery(q)
              setAttachPicked(null)
            }}
            picked={attachPicked}
            onPick={(c) => {
              setAttachPicked(c)
              setAttachQuery(c.fullName)
            }}
            onClear={() => {
              setAttachPicked(null)
              setAttachQuery('')
            }}
            hits={attachCustomersQuery.data ?? []}
            hitsLoading={attachCustomersQuery.isFetching}
            zoneId={selected?.zoneId}
          />
          {sessionError && <p className="error">{sessionError}</p>}
        </div>
      </ActionDialog>
      <ConfirmDialog
        open={endConfirm === 'end'}
        onClose={() => setEndConfirm(null)}
        title="Завершить сеанс?"
        message="Сеанс будет закрыт. Оставшееся время не сохранится."
        confirmLabel="Завершить"
        danger
        loading={endMutation.isPending}
        onConfirm={() => endMutation.mutate({})}
      />
      <ConfirmDialog
        open={endConfirm === 'save'}
        onClose={() => setEndConfirm(null)}
        title="Сохранить минуты клиенту?"
        message={`Сеанс закроется, остаток (~${Math.floor((activeSessionQuery.data?.remainingSeconds ?? 0) / 60)} мин) попадёт в банк минут клиента по этой зоне, не на денежный баланс.`}
        confirmLabel="Завершить и сохранить"
        loading={endMutation.isPending}
        onConfirm={() => endMutation.mutate({ saveRemaining: true })}
      />
      <ConfirmDialog
        open={voenkomatOpen}
        onClose={() => !voenkomatMutation.isPending && setVoenkomatOpen(false)}
        title="Отправить сигнал ВОЕНКОМАТ?"
        message={`Красное сообщение со звуком появится ПОВЕРХ игр на всех подтверждённых ПК (${
          (computersQuery.data ?? []).filter((c) => c.isApproved).length
        }): «Срочно зайдите к администратору». Отправить?`}
        confirmLabel="Отправить всем"
        danger
        loading={voenkomatMutation.isPending}
        onConfirm={() => voenkomatMutation.mutate()}
      />
      <ConfirmDialog
        open={!!techActionConfirm}
        onClose={() => setTechActionConfirm(null)}
        title="Подтвердите действие"
        message={
          techActionConfirm?.type === 'RunCmd'
            ? techTargets.length > 1
              ? `Выполнить CMD на ${techTargets.length} ПК?\n\n${techCmdLine.trim()}`
              : `Выполнить CMD на «${
                  techTargets[0]?.displayName ?? techTargets[0]?.windowsName ?? 'ПК'
                }»?\n\n${techCmdLine.trim()}`
            : techTargets.length > 1
              ? `Точно ${techActionConfirm?.label ?? 'выполнить команду'}? Выбрано: ${techTargets
                  .slice(0, 5)
                  .map((c) => c.displayName ?? c.windowsName)
                  .join(', ')}${techTargets.length > 5 ? '…' : ''}.`
              : `Точно ${techActionConfirm?.label ?? 'выполнить команду'} на «${
                  techTargets[0]?.displayName ?? techTargets[0]?.windowsName ?? 'ПК'
                }»?`
        }
        confirmLabel={techTargets.length > 1 ? `Выполнить · ${techTargets.length}` : 'Выполнить'}
        danger={
          techActionConfirm?.type === 'Shutdown' ||
          techActionConfirm?.type === 'Restart' ||
          techActionConfirm?.type === 'RunCmd'
        }
        loading={commandMutation.isPending || wakeMutation.isPending || techCmdRunning}
        onConfirm={() => {
          if (!techActionConfirm) return
          const type = techActionConfirm.type
          const payloadJson = techActionConfirm.payloadJson
          setTechActionConfirm(null)
          if (type === 'Wake') {
            wakeMutation.mutate(
              techTargets
                .filter((c) => !!c.macAddress?.trim() && !c.currentSessionId)
                .map((c) => c.id),
            )
            return
          }
          if (type === 'RunCmd') {
            void runRemoteCmdOnTargets(techCmdLine, techTargets.map((c) => c.id)).catch((e) =>
              setSessionError(e instanceof Error ? e.message : 'Ошибка CMD'),
            )
            return
          }
          let ids = techTargets.map((c) => c.id)
          if (type === 'Lock') {
            ids = techTargets
              .filter((c) => c.status !== 'Locked' && c.status !== 3)
              .map((c) => c.id)
          } else if (type === 'Unlock') {
            ids = techTargets
              .filter((c) => c.status === 'Locked' || c.status === 3)
              .map((c) => c.id)
          }
          if (ids.length === 0) {
            setSessionError('Нет подходящих ПК для этой команды')
            return
          }
          commandMutation.mutate({ type, payloadJson, computerIds: ids })
        }}
      />
    </>
  )
}

