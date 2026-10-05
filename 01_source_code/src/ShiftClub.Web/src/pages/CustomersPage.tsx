import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { apiFetch, newIdempotencyKey } from '../api/client'
import type { ZoneDto } from '../api/client'
import { ActionDialog } from '../components/ActionDialog'
import { CASE_KEY_DEFAULT_PRICE } from '../components/FloorQuickCaseKeyDialog'
import {
  formatDuration,
  isCredit,
  ledgerTypeLabel,
  timeBankReasonLabel,
} from '../format'
import { can, Perm } from '../permissions'

const CasePrizeType = {
  Time: 1,
  Balance: 2,
  BarItem: 3,
  Discount: 4,
  Service: 5,
} as const

const CaseRewardStatus = {
  Pending: 1,
  Applied: 2,
  Claimed: 3,
} as const

type CasePrizeModal = {
  prizeName: string
  displayName?: string
  imageUrl?: string | null
  applyMessage?: string | null
  keysRemaining?: number | null
  rewardId?: string | null
  prizeType?: number | null
  rewardStatus?: number | null
  payloadJson?: string | null
  customerId?: string | null
}

function casePayloadHasTariffKind(payloadJson?: string | null): boolean {
  if (!payloadJson) return false
  try {
    const p = JSON.parse(payloadJson) as { tariffKind?: unknown }
    return p.tariffKind != null && String(p.tariffKind).length > 0
  } catch {
    return false
  }
}

function casePrizeNeedsZone(prizeType?: number | null, payloadJson?: string | null): boolean {
  if (prizeType === CasePrizeType.Time) return true
  if (prizeType === CasePrizeType.Service && !casePayloadHasTariffKind(payloadJson)) return true
  return false
}

function caseApplyButtonLabel(prizeType?: number | null, payloadJson?: string | null): string {
  if (casePrizeNeedsZone(prizeType, payloadJson)) return 'Зачислить время'
  if (prizeType === CasePrizeType.Balance) return 'Зачислить на баланс'
  if (prizeType === CasePrizeType.Discount) return 'Активировать скидку'
  if (prizeType === CasePrizeType.BarItem) return 'Выдано на баре'
  if (prizeType === CasePrizeType.Service) return 'Выдано'
  return 'Зачислить приз'
}

type CustomerDto = {
  id: string
  firstName: string
  lastName: string
  fullName: string
  phone: string
  email?: string
  balance: number
  bonusBalance: number
  timeBankMinutes?: number
  timeBanks?: { zoneId: string; zoneName: string; minutes: number }[]
  loyaltyLevelName?: string
  loyaltyLevelId?: string | null
  loyaltyBonusPercent?: number
  loyaltyTimeDiscountPercent?: number
  loyaltyLevelLocked?: boolean
  visitCount: number
  totalSpent: number
  isBlocked: boolean
  blockReason?: string
  isActive: boolean
  createdAt?: string
  notes?: string
  login?: string
  hasPassword?: boolean
  hasPin?: boolean
  caseKeysBalance?: number
}

type TxDto = {
  id: string
  type: string | number
  direction: string | number
  amount: number
  balanceBefore: number
  balanceAfter: number
  comment?: string
  createdAt: string
}

type TimeBankTxDto = {
  id: string
  zoneId?: string
  zoneName?: string
  reason: string | number
  direction: string | number
  minutes: number
  balanceBefore: number
  balanceAfter: number
  comment?: string
  createdAt: string
}

type ProfileForm = {
  firstName: string
  lastName: string
  phone: string
  email: string
  notes: string
  isBlocked: boolean
  blockReason: string
  loyaltyLevelId: string
  loyaltyLevelLocked: boolean
}

type LoyaltyLevelOption = {
  id: string
  name: string
  code: string
  bonusPercent: number
  timeDiscountPercent: number
  isActive: boolean
}

type DepositQuoteDto = {
  amount: number
  loyaltyBonusPercent: number
  loyaltyBonusAmount: number
  tierMinAmount: number
  tierBonusAmount: number
  totalBonusAmount: number
  depositBonusEnabled: boolean
  stackWithLoyaltyPercent: boolean
  balanceAfterDeposit: number
  bonusBalanceAfterDeposit: number
}

type DepositResultDto = {
  customer: CustomerDto
  quote: DepositQuoteDto
}

type CardTab = 'profile' | 'money' | 'time' | 'case' | 'access' | 'history'

const CUSTOMER_HISTORY_PAGE = 100

function readCaseKeysBalance(c: CustomerDto | null | undefined): number {
  if (!c) return 0
  const raw = c as CustomerDto & { CaseKeysBalance?: number }
  return raw.caseKeysBalance ?? raw.CaseKeysBalance ?? 0
}

const emptyProfile: ProfileForm = {
  firstName: '',
  lastName: '',
  phone: '',
  email: '',
  notes: '',
  isBlocked: false,
  blockReason: '',
  loyaltyLevelId: '',
  loyaltyLevelLocked: false,
}

function toProfile(c: CustomerDto): ProfileForm {
  return {
    firstName: c.firstName ?? '',
    lastName: c.lastName ?? '',
    phone: c.phone ?? '',
    email: c.email ?? '',
    notes: c.notes ?? '',
    isBlocked: !!c.isBlocked,
    blockReason: c.blockReason ?? '',
    loyaltyLevelId: c.loyaltyLevelId ?? '',
    loyaltyLevelLocked: !!c.loyaltyLevelLocked,
  }
}

export function CustomersPage() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [cardOpen, setCardOpen] = useState(false)
  const [cardTab, setCardTab] = useState<CardTab>('profile')
  const [error, setError] = useState<string | null>(null)
  const [flash, setFlash] = useState<string | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [depositOpen, setDepositOpen] = useState(false)
  const [buyKeysOpen, setBuyKeysOpen] = useState(false)
  const [grantKeysOpen, setGrantKeysOpen] = useState(false)
  const [adjustOpen, setAdjustOpen] = useState(false)
  const [timeBankOpen, setTimeBankOpen] = useState(false)
  const [casePrizeOpen, setCasePrizeOpen] = useState(false)
  const [casePrize, setCasePrize] = useState<CasePrizeModal | null>(null)
  const [casePrizeZoneId, setCasePrizeZoneId] = useState('')
  const [casePrizeApplyMsg, setCasePrizeApplyMsg] = useState<string | null>(null)
  const [deskWaitCmdId, setDeskWaitCmdId] = useState<string | null>(null)

  const [createForm, setCreateForm] = useState({
    firstName: '',
    lastName: '',
    phone: '',
    email: '',
    pin: '',
  })
  const [profile, setProfile] = useState<ProfileForm>(emptyProfile)
  const [credLogin, setCredLogin] = useState('')
  const [credPassword, setCredPassword] = useState('')
  const [credPin, setCredPin] = useState('')
  const [clearPassword, setClearPassword] = useState(false)
  const [clearPin, setClearPin] = useState(false)

  const [depositAmount, setDepositAmount] = useState(1000)
  const [depositMethod, setDepositMethod] = useState<'Cash' | 'KaspiQr'>('Cash')
  const [buyKeysQty, setBuyKeysQty] = useState(1)
  const [buyKeysPrice, setBuyKeysPrice] = useState(CASE_KEY_DEFAULT_PRICE)
  const [buyKeysMethod, setBuyKeysMethod] = useState<'Cash' | 'KaspiQr' | 'Card'>('Cash')
  const [grantKeysQty, setGrantKeysQty] = useState(1)
  const [grantKeysComment, setGrantKeysComment] = useState('')
  const [adjustAmount, setAdjustAmount] = useState(100)
  const [adjustDirection, setAdjustDirection] = useState<'Credit' | 'Debit'>('Credit')
  const [adjustComment, setAdjustComment] = useState('')
  const [timeBankMinutes, setTimeBankMinutes] = useState(30)
  const [timeBankDirection, setTimeBankDirection] = useState<'Credit' | 'Debit'>('Credit')
  const [timeBankComment, setTimeBankComment] = useState('')
  const [timeBankZoneId, setTimeBankZoneId] = useState('')
  const [historyTab, setHistoryTab] = useState<'money' | 'time'>('money')
  const [moneyHistoryTake, setMoneyHistoryTake] = useState(CUSTOMER_HISTORY_PAGE)
  const [timeHistoryTake, setTimeHistoryTake] = useState(CUSTOMER_HISTORY_PAGE)

  const canDeposit = can(Perm.CustomersDeposit, Perm.CustomersManage)
  const canAdjust = can(Perm.CustomersAdjust, Perm.CustomersManage)
  const canManageCustomers = can(Perm.CustomersManage)

  const zonesQuery = useQuery({
    queryKey: ['zones', 'customers'],
    queryFn: async () => (await apiFetch<ZoneDto[]>('/api/zones')).data ?? [],
  })
  const zones = (zonesQuery.data ?? []).filter((z) => z.isActive)

  const loyaltyLevelsQuery = useQuery({
    queryKey: ['loyalty-levels'],
    queryFn: async () =>
      (await apiFetch<LoyaltyLevelOption[]>('/api/loyalty-levels')).data ?? [],
  })

  const customersQuery = useQuery({
    queryKey: ['customers', query],
    queryFn: async () =>
      (await apiFetch<CustomerDto[]>(`/api/customers?q=${encodeURIComponent(query)}`)).data ?? [],
  })

  const detailQuery = useQuery({
    queryKey: ['customer', selectedId],
    queryFn: async () => (await apiFetch<CustomerDto>(`/api/customers/${selectedId}`)).data!,
    enabled: !!selectedId && cardOpen,
  })

  const listItem = useMemo(
    () => customersQuery.data?.find((c) => c.id === selectedId) ?? null,
    [customersQuery.data, selectedId],
  )
  const detail = detailQuery.data ?? listItem
  const bankMinutes = detail?.timeBankMinutes ?? 0
  const zoneBanks = detail?.timeBanks ?? []
  const selectedZoneBank = zoneBanks.find((b) => b.zoneId === timeBankZoneId)?.minutes ?? 0

  useEffect(() => {
    const c = detailQuery.data
    if (!c || !cardOpen) return
    setProfile(toProfile(c))
    setCredLogin(c.login ?? '')
    setCredPassword('')
    setCredPin('')
    setClearPassword(false)
    setClearPin(false)
  }, [detailQuery.data, cardOpen])

  const txQuery = useQuery({
    queryKey: ['customer-tx', selectedId, moneyHistoryTake],
    queryFn: async () =>
      (await apiFetch<TxDto[]>(`/api/customers/${selectedId}/transactions?take=${moneyHistoryTake}`)).data ??
      [],
    enabled:
      !!selectedId &&
      cardOpen &&
      (cardTab === 'money' || (cardTab === 'history' && historyTab === 'money')),
  })

  const timeBankTxQuery = useQuery({
    queryKey: ['customer-timebank-tx', selectedId, timeHistoryTake],
    queryFn: async () =>
      (
        await apiFetch<TimeBankTxDto[]>(
          `/api/customers/${selectedId}/time-bank/transactions?take=${timeHistoryTake}`,
        )
      ).data ?? [],
    enabled:
      !!selectedId &&
      cardOpen &&
      (cardTab === 'time' || (cardTab === 'history' && historyTab === 'time')),
  })

  async function refreshCustomer() {
    await queryClient.invalidateQueries({ queryKey: ['customers'] })
    await queryClient.invalidateQueries({ queryKey: ['customer', selectedId] })
    await queryClient.invalidateQueries({ queryKey: ['customer-tx', selectedId] })
    await queryClient.invalidateQueries({ queryKey: ['customer-timebank-tx', selectedId] })
  }

  function openCard(id: string) {
    setSelectedId(id)
    setCardTab('profile')
    setHistoryTab('money')
    setMoneyHistoryTake(CUSTOMER_HISTORY_PAGE)
    setTimeHistoryTake(CUSTOMER_HISTORY_PAGE)
    setError(null)
    setFlash(null)
    setCardOpen(true)
  }

  function closeCard() {
    setCardOpen(false)
    setSelectedId(null)
  }

  async function openCaseOnDisplay() {
    if (!detail?.id) return
    setError(null)
    try {
      const res = await apiFetch<{
        commandId: string
        customerId: string
        displayName: string
        phone: string
        displayToken?: string
      }>(`/api/cases/desk-show/${detail.id}`, { method: 'POST', body: '{}' })
      const cmd = res.data
      const payload = {
        commandId: cmd?.commandId,
        customerId: cmd?.customerId ?? detail.id,
        displayName: cmd?.displayName ?? detail.fullName,
        phone: cmd?.phone ?? detail.phone,
        displayToken: cmd?.displayToken,
        at: Date.now(),
      }
      try {
        localStorage.setItem('shift.deskDisplay.cmd', JSON.stringify(payload))
        const bc = new BroadcastChannel('shift.deskDisplay')
        bc.postMessage(payload)
        bc.close()
      } catch {
        /* ignore */
      }
      if (cmd?.commandId) setDeskWaitCmdId(cmd.commandId)
      setFlash('Кейс на экране акции. Ждём результат — покажем здесь, что выпало.')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Не удалось отправить на экран')
    }
  }

  function showCasePrize(result: CasePrizeModal) {
    if (result.customerId) {
      setSelectedId(result.customerId)
      setCardTab('case')
      setCardOpen(true)
    }
    setCasePrizeApplyMsg(null)
    setError(null)
    setCasePrizeZoneId(zones[0]?.id ?? '')
    setCasePrize({
      prizeName: result.prizeName,
      displayName: result.displayName,
      imageUrl: result.imageUrl,
      applyMessage: result.applyMessage,
      keysRemaining: result.keysRemaining,
      rewardId: result.rewardId,
      prizeType: result.prizeType,
      rewardStatus: result.rewardStatus ?? CaseRewardStatus.Pending,
      payloadJson: result.payloadJson,
      customerId: result.customerId,
    })
    setCasePrizeOpen(true)
    setFlash(`Приз кейса: ${result.prizeName}`)
    void queryClient.invalidateQueries({ queryKey: ['customers'] })
    if (result.customerId) {
      void queryClient.invalidateQueries({ queryKey: ['customer', result.customerId] })
    }
  }

  useEffect(() => {
    if (!casePrizeOpen || casePrizeZoneId || zones.length === 0) return
    setCasePrizeZoneId(zones[0]!.id)
  }, [casePrizeOpen, casePrizeZoneId, zones])

  // Возврат с /site/case.html?desk=1 → /customers?c=…&casePrize=…
  useEffect(() => {
    const cid = searchParams.get('c')
    const prize = searchParams.get('casePrize')
    if (!prize && !cid) return
    if (prize) {
      const prizeTypeRaw = searchParams.get('caseType')
      const statusRaw = searchParams.get('caseStatus')
      showCasePrize({
        prizeName: prize,
        customerId: cid,
        imageUrl: searchParams.get('caseImg'),
        applyMessage: searchParams.get('caseMsg'),
        rewardId: searchParams.get('caseRewardId'),
        prizeType: prizeTypeRaw ? Number(prizeTypeRaw) : null,
        rewardStatus: statusRaw ? Number(statusRaw) : CaseRewardStatus.Pending,
        payloadJson: searchParams.get('casePayload'),
      })
    } else if (cid) {
      openCard(cid)
    }
    setSearchParams({}, { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // После «Открыть кейс» ждём результат с экрана акции
  useEffect(() => {
    if (!deskWaitCmdId) return
    let stopped = false
    const cmdId = deskWaitCmdId
    const started = Date.now()

    const tick = async () => {
      if (stopped) return
      try {
        const res = await apiFetch<{
          commandId: string
          customerId: string
          displayName: string
          rewardId: string
          prizeName: string
          prizeType: number
          payloadJson: string
          rewardStatus: number
          imageUrl?: string
          applyMessage?: string
          keysRemaining?: number
        } | null>(`/api/cases/desk-result?commandId=${encodeURIComponent(cmdId)}`)
        const d = res.data
        if (d?.prizeName) {
          setDeskWaitCmdId(null)
          navigate('/customers', { replace: true })
          showCasePrize({
            prizeName: d.prizeName,
            displayName: d.displayName,
            imageUrl: d.imageUrl,
            applyMessage: d.applyMessage,
            keysRemaining: d.keysRemaining,
            customerId: d.customerId,
            rewardId: d.rewardId,
            prizeType: d.prizeType,
            rewardStatus: d.rewardStatus,
            payloadJson: d.payloadJson,
          })
          return
        }
      } catch {
        /* keep polling */
      }
      if (Date.now() - started > 120_000) {
        setDeskWaitCmdId(null)
        setFlash('Кейс отправлен на экран. Результат не получен — обновите карточку гостя.')
        return
      }
      window.setTimeout(() => void tick(), 1500)
    }
    void tick()
    return () => {
      stopped = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [deskWaitCmdId])

  const createMutation = useMutation({
    mutationFn: async () =>
      apiFetch<CustomerDto>('/api/customers', {
        method: 'POST',
        body: JSON.stringify({
          firstName: createForm.firstName,
          lastName: createForm.lastName,
          phone: createForm.phone,
          email: createForm.email || null,
          notes: null,
          pin: createForm.pin.trim() || null,
        }),
      }),
    onSuccess: async (res) => {
      setError(null)
      setFlash(
        'Клиент создан. Ключ SHIFT CASE начислен — откройте кейс на кассе (рулетка).',
      )
      setCreateForm({ firstName: '', lastName: '', phone: '', email: '', pin: '' })
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['customers'] })
      if (res.data?.id) openCard(res.data.id)
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка создания'),
  })

  const updateMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Клиент не выбран')
      return apiFetch(`/api/customers/${selectedId}`, {
        method: 'PUT',
        body: JSON.stringify({
          firstName: profile.firstName.trim(),
          lastName: profile.lastName.trim(),
          phone: profile.phone.trim(),
          email: profile.email.trim() || null,
          notes: profile.notes.trim() || null,
          isBlocked: profile.isBlocked,
          blockReason: profile.isBlocked ? profile.blockReason.trim() || null : null,
          // empty → Guid.Empty на сервере = снять уровень; иначе назначить выбранный
          loyaltyLevelId: profile.loyaltyLevelId || '00000000-0000-0000-0000-000000000000',
          loyaltyLevelLocked: profile.loyaltyLevelLocked,
        }),
      })
    },
    onSuccess: async () => {
      setError(null)
      setFlash('Данные сохранены')
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка сохранения'),
  })

  const depositMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Выберите клиента')
      return apiFetch<DepositResultDto>(`/api/customers/${selectedId}/deposit`, {
        method: 'POST',
        body: JSON.stringify({
          amount: depositAmount,
          paymentMethod: depositMethod,
          comment: 'Пополнение с панели',
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async (res) => {
      setError(null)
      const bonus = Number(res.data?.quote?.totalBonusAmount ?? 0)
      setFlash(
        bonus > 0
          ? `Пополнено ${depositAmount.toLocaleString('ru-RU')} ₸ · бонус +${bonus.toLocaleString('ru-RU')} ₸`
          : `Пополнено ${depositAmount.toLocaleString('ru-RU')} ₸`,
      )
      setDepositOpen(false)
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка пополнения'),
  })

  const buyKeysMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Выберите клиента')
      return apiFetch<{
        receiptNumber: string
        keysGranted: number
        keysBalance: number
        paidTotal: number
      }>('/api/cases/buy-keys', {
        method: 'POST',
        body: JSON.stringify({
          customerId: selectedId,
          quantity: buyKeysQty,
          unitPrice: buyKeysPrice,
          paymentMethod: buyKeysMethod,
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async (res) => {
      setError(null)
      const d = res.data
      setFlash(
        d
          ? `Ключи: +${d.keysGranted} · чек ${d.receiptNumber} · ${d.paidTotal.toLocaleString('ru-RU')} ₸ · остаток ${d.keysBalance}`
          : 'Ключи куплены',
      )
      setBuyKeysOpen(false)
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Не удалось продать ключи'),
  })

  const grantKeysMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Выберите клиента')
      return apiFetch('/api/cases/grant-keys', {
        method: 'POST',
        body: JSON.stringify({
          customerId: selectedId,
          keys: grantKeysQty,
          comment: grantKeysComment.trim(),
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async () => {
      setError(null)
      setFlash('Ключи начислены')
      setGrantKeysOpen(false)
      setGrantKeysComment('')
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Не удалось начислить ключи'),
  })

  const depositQuoteQuery = useQuery({
    queryKey: ['deposit-quote', selectedId, depositAmount],
    queryFn: async () =>
      (
        await apiFetch<DepositQuoteDto>(
          `/api/customers/${selectedId}/deposit-quote?amount=${encodeURIComponent(String(depositAmount))}`,
        )
      ).data!,
    enabled: depositOpen && !!selectedId && depositAmount >= 100,
    staleTime: 1500,
  })

  const depositQuote = depositQuoteQuery.data

  const adjustMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Выберите клиента')
      if (!adjustComment.trim()) throw new Error('Укажите комментарий')
      return apiFetch(`/api/customers/${selectedId}/adjust`, {
        method: 'POST',
        body: JSON.stringify({
          amount: adjustAmount,
          direction: adjustDirection,
          comment: adjustComment.trim(),
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async () => {
      setError(null)
      setFlash(adjustDirection === 'Credit' ? 'Баланс увеличен' : 'Баланс уменьшен')
      setAdjustOpen(false)
      setAdjustComment('')
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка корректировки'),
  })

  const caseApplyMutation = useMutation({
    mutationFn: async () => {
      if (!casePrize?.rewardId) throw new Error('Нет ID награды — обновите карточку клиента')
      const res = await apiFetch<{ message: string; status: number }>(
        `/api/cases/rewards/${casePrize.rewardId}/apply`,
        {
          method: 'POST',
          body: JSON.stringify({
            zoneId: casePrizeNeedsZone(casePrize.prizeType, casePrize.payloadJson)
              ? casePrizeZoneId || null
              : null,
          }),
        },
      )
      if (!res.success) throw new Error(res.message || 'Не удалось зачислить')
      return res.data!
    },
    onSuccess: (d) => {
      setCasePrizeApplyMsg(d.message)
      setCasePrize((p) =>
        p
          ? {
              ...p,
              rewardStatus: d.status,
              applyMessage: d.message,
            }
          : p,
      )
      setFlash(d.message)
      void refreshCustomer()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Ошибка зачисления'),
  })

  const timeBankMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Выберите клиента')
      if (!timeBankComment.trim()) throw new Error('Укажите комментарий')
      return apiFetch(`/api/customers/${selectedId}/time-bank/adjust`, {
        method: 'POST',
        body: JSON.stringify({
          zoneId: timeBankZoneId,
          minutes: timeBankMinutes,
          direction: timeBankDirection,
          comment: timeBankComment.trim(),
          idempotencyKey: newIdempotencyKey(),
        }),
      })
    },
    onSuccess: async () => {
      setError(null)
      setFlash(timeBankDirection === 'Credit' ? 'Минуты начислены' : 'Минуты списаны')
      setTimeBankOpen(false)
      setTimeBankComment('')
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка банка времени'),
  })

  const credMutation = useMutation({
    mutationFn: async () => {
      if (!selectedId) throw new Error('Клиент не выбран')
      return apiFetch(`/api/customers/${selectedId}/credentials`, {
        method: 'POST',
        body: JSON.stringify({
          login: credLogin.trim() || null,
          password: clearPassword ? null : credPassword.trim() || null,
          pin: clearPin ? null : credPin.trim() || null,
          clearPassword,
          clearPin,
        }),
      })
    },
    onSuccess: async () => {
      setError(null)
      setFlash('Доступ обновлён')
      setCredPassword('')
      setCredPin('')
      setClearPassword(false)
      setClearPin(false)
      await refreshCustomer()
    },
    onError: (err) => setError(err instanceof Error ? err.message : 'Ошибка учётных данных'),
  })

  const profileDirty =
    detail &&
    (profile.firstName !== (detail.firstName ?? '') ||
      profile.lastName !== (detail.lastName ?? '') ||
      profile.phone !== (detail.phone ?? '') ||
      profile.email !== (detail.email ?? '') ||
      profile.notes !== (detail.notes ?? '') ||
      profile.isBlocked !== !!detail.isBlocked ||
      profile.blockReason !== (detail.blockReason ?? '') ||
      profile.loyaltyLevelId !== (detail.loyaltyLevelId ?? '') ||
      profile.loyaltyLevelLocked !== !!detail.loyaltyLevelLocked)

  return (
    <>
      <header className="page-head">
        <div>
          <h1>Клиенты</h1>
          <p className="muted" style={{ margin: 0, fontSize: 13 }}>
            Поиск · карточка в окне · данные аккаунта · баланс · банк · ПИН
          </p>
        </div>
        {canManageCustomers && (
          <button
            type="button"
            onClick={() => {
              setError(null)
              setCreateOpen(true)
            }}
          >
            + Новый клиент
          </button>
        )}
      </header>
      {customersQuery.isError && <p className="error">{(customersQuery.error as Error).message}</p>}
      {flash && !cardOpen && <p className="flash">{flash}</p>}

      <section className="list-card">
        <div className="page-toolbar" style={{ padding: '8px 4px' }}>
          <label style={{ flex: 1, maxWidth: 420 }}>
            Поиск
            <input
              placeholder="Телефон, имя, фамилия или логин"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </label>
          <span className="muted" style={{ alignSelf: 'flex-end', paddingBottom: 8, fontSize: 13 }}>
            {customersQuery.data?.length ?? 0} чел.
          </span>
        </div>

        {customersQuery.isLoading && <p style={{ padding: 12 }}>Загрузка…</p>}
        <ul className="computers-rows customer-rows" style={{ margin: '8px 4px 12px' }}>
          {customersQuery.data?.map((c) => (
            <li key={c.id}>
              <button type="button" className={`computers-row customer-row${c.isBlocked ? ' is-blocked' : ''}`} onClick={() => openCard(c.id)}>
                <div className="computers-row-main">
                  <div className="computers-row-title">
                    <strong>{c.fullName}</strong>
                    {c.isBlocked && <span className="chip chip-warn">Блок</span>}
                    {c.loyaltyLevelName && <span className="chip">{c.loyaltyLevelName}</span>}
                  </div>
                  <div className="computers-row-status customer-balance-chips">
                    <span className="chip chip--ok">{c.balance.toLocaleString('ru-RU')} ₸</span>
                    {c.bonusBalance > 0 && (
                      <span className="chip chip-bonus">+{c.bonusBalance.toLocaleString('ru-RU')} бон.</span>
                    )}
                    {(c.timeBankMinutes ?? 0) > 0 && (
                      <span className="chip chip-time">{formatDuration(c.timeBankMinutes ?? 0)}</span>
                    )}
                    {readCaseKeysBalance(c) > 0 && (
                      <span className="chip">CASE · {readCaseKeysBalance(c)}</span>
                    )}
                  </div>
                  <div className="computers-row-meta muted">
                    <span>{c.phone}</span>
                    <span>визитов: {c.visitCount}</span>
                    <span className="customer-row-hint">Открыть карточку →</span>
                  </div>
                </div>
              </button>
            </li>
          ))}
          {!customersQuery.isLoading && (customersQuery.data?.length ?? 0) === 0 && (
            <li className="muted cash-receipt-empty">Ничего не найдено</li>
          )}
        </ul>
      </section>

      <ActionDialog
        open={cardOpen && !!detail}
        onClose={() => {
          if (
            updateMutation.isPending ||
            depositMutation.isPending ||
            adjustMutation.isPending ||
            timeBankMutation.isPending ||
            credMutation.isPending
          )
            return
          closeCard()
        }}
        title={detail?.fullName ?? 'Клиент'}
        description={
          detail
            ? `${detail.phone}${detail.email ? ` · ${detail.email}` : ''}${detail.loyaltyLevelName ? ` · ${detail.loyaltyLevelName}` : ''}`
            : undefined
        }
        size="xl"
        footer={
          <button type="button" className="ghost" onClick={closeCard}>
            Закрыть
          </button>
        }
      >
        {detail && (
          <>
            <div className="customer-stat-grid">
              <div className="customer-stat">
                <span>Баланс</span>
                <strong>{detail.balance.toLocaleString('ru-RU')} ₸</strong>
              </div>
              <div className="customer-stat">
                <span>Бонусы</span>
                <strong>{detail.bonusBalance.toLocaleString('ru-RU')} ₸</strong>
              </div>
              <div className="customer-stat">
                <span>Банк времени</span>
                <strong>{formatDuration(bankMinutes)}</strong>
              </div>
              <div className="customer-stat">
                <span>Потрачено</span>
                <strong>{detail.totalSpent.toLocaleString('ru-RU')} ₸</strong>
              </div>
              <div className="customer-stat">
                <span>Ключи CASE</span>
                <strong>{readCaseKeysBalance(detail)}</strong>
              </div>
            </div>

            {flash && <p className="flash">{flash}</p>}
            {error && <p className="error">{error}</p>}

            <div className="customer-card-tabs" role="tablist">
              {(
                [
                  ['profile', 'Данные'],
                  ['money', 'Баланс'],
                  ['time', 'Банк'],
                  ['case', 'CASE'],
                  ['access', 'Вход'],
                  ['history', 'История'],
                ] as const
              ).map(([id, label]) => (
                <button
                  key={id}
                  type="button"
                  role="tab"
                  aria-selected={cardTab === id}
                  className={cardTab === id ? undefined : 'ghost'}
                  onClick={() => {
                    setCardTab(id)
                    setError(null)
                    if (id === 'history') setHistoryTab('money')
                  }}
                >
                  {label}
                </button>
              ))}
            </div>

            {cardTab === 'profile' && (
              <div className="form-stack">
                <div className="field-grid">
                  <label>
                    Имя
                    <input
                      value={profile.firstName}
                      onChange={(e) => setProfile((p) => ({ ...p, firstName: e.target.value }))}
                    />
                  </label>
                  <label>
                    Фамилия
                    <input
                      value={profile.lastName}
                      onChange={(e) => setProfile((p) => ({ ...p, lastName: e.target.value }))}
                    />
                  </label>
                </div>
                <div className="field-grid">
                  <label>
                    Телефон
                    <input
                      value={profile.phone}
                      onChange={(e) => setProfile((p) => ({ ...p, phone: e.target.value }))}
                      placeholder="77001234567"
                    />
                  </label>
                  <label>
                    Email
                    <input
                      value={profile.email}
                      onChange={(e) => setProfile((p) => ({ ...p, email: e.target.value }))}
                      placeholder="optional"
                    />
                  </label>
                </div>
                <label>
                  Заметки
                  <textarea
                    rows={3}
                    value={profile.notes}
                    onChange={(e) => setProfile((p) => ({ ...p, notes: e.target.value }))}
                    placeholder="Комментарий администратора"
                    style={{ resize: 'vertical', minHeight: 72 }}
                  />
                </label>
                <div className="field-grid">
                  <label>
                    Уровень лояльности
                    <select
                      value={profile.loyaltyLevelId}
                      onChange={(e) =>
                        setProfile((p) => ({ ...p, loyaltyLevelId: e.target.value }))
                      }
                    >
                      <option value="">— не задан —</option>
                      {(loyaltyLevelsQuery.data ?? []).map((l) => (
                        <option key={l.id} value={l.id}>
                          {l.name} · бонус {l.bonusPercent}% · время −{l.timeDiscountPercent}%
                          {!l.isActive ? ' (выкл)' : ''}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="check-row" style={{ alignSelf: 'end', marginBottom: 4 }}>
                    <input
                      type="checkbox"
                      checked={profile.loyaltyLevelLocked}
                      onChange={(e) =>
                        setProfile((p) => ({ ...p, loyaltyLevelLocked: e.target.checked }))
                      }
                    />
                    Зафиксировать уровень (не менять автоматически)
                  </label>
                </div>
                {(detail.loyaltyBonusPercent || detail.loyaltyTimeDiscountPercent) && (
                  <p className="muted" style={{ margin: 0, fontSize: 12 }}>
                    Сейчас: бонус при пополнении {detail.loyaltyBonusPercent ?? 0}% · скидка на
                    время {detail.loyaltyTimeDiscountPercent ?? 0}%
                    {detail.loyaltyLevelLocked ? ' · уровень зафиксирован' : ''}
                  </p>
                )}
                <label className="check-row">
                  <input
                    type="checkbox"
                    checked={profile.isBlocked}
                    onChange={(e) => setProfile((p) => ({ ...p, isBlocked: e.target.checked }))}
                  />
                  Заблокировать аккаунт
                </label>
                {profile.isBlocked && (
                  <label>
                    Причина блокировки
                    <input
                      value={profile.blockReason}
                      onChange={(e) => setProfile((p) => ({ ...p, blockReason: e.target.value }))}
                      placeholder="Например: нарушение правил"
                    />
                  </label>
                )}
                <p className="muted" style={{ margin: 0, fontSize: 12 }}>
                  Визитов: {detail.visitCount}
                  {detail.createdAt
                    ? ` · создан ${new Date(detail.createdAt).toLocaleDateString('ru-RU')}`
                    : ''}
                </p>
                <div className="customer-card-actions">
                  <button
                    type="button"
                    disabled={
                      updateMutation.isPending ||
                      !profile.firstName.trim() ||
                      !profile.phone.trim() ||
                      !profileDirty
                    }
                    onClick={() => updateMutation.mutate()}
                  >
                    {updateMutation.isPending ? 'Сохранение…' : 'Сохранить данные'}
                  </button>
                  {profileDirty && (
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => setProfile(toProfile(detail))}
                      disabled={updateMutation.isPending}
                    >
                      Отменить правки
                    </button>
                  )}
                </div>
              </div>
            )}

            {cardTab === 'money' && (
              <div>
                <div className="customer-card-actions">
                  {canDeposit && (
                    <button
                      type="button"
                      onClick={() => {
                        setError(null)
                        setDepositOpen(true)
                      }}
                    >
                      Пополнить
                    </button>
                  )}
                  {canAdjust && (
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => {
                        setError(null)
                        setAdjustAmount(100)
                        setAdjustDirection('Credit')
                        setAdjustComment('')
                        setAdjustOpen(true)
                      }}
                    >
                      Корректировка
                    </button>
                  )}
                </div>
                <p className="muted" style={{ fontSize: 13, marginTop: 0 }}>
                  {(detail.loyaltyBonusPercent ?? 0) > 0 && (
                    <>При пополнении уровень даёт +{detail.loyaltyBonusPercent}% на бонусный счёт.</>
                  )}
                </p>
                <h3 style={{ margin: '8px 0 10px', fontSize: '1rem' }}>Последние операции</h3>
                <p className="muted" style={{ fontSize: 12, margin: '0 0 10px' }}>
                  Движения по балансу и бонусам. Оплата сеанса наличными / Kaspi — в разделе «Касса → Чеки».
                </p>
                {txQuery.isLoading && <p className="muted">Загрузка…</p>}
                <div className="customer-history-panel">
                  <ul className="customer-history-list">
                    {txQuery.data?.map((t) => (
                      <li key={t.id}>
                        <div className="hist-main">
                          <strong>
                            {isCredit(t.direction) ? '+' : '−'}
                            {t.amount.toLocaleString('ru-RU')} ₸
                          </strong>
                          <span className="muted">{new Date(t.createdAt).toLocaleString('ru-RU')}</span>
                        </div>
                        <span className="muted">
                          {ledgerTypeLabel(t.type)}
                          {t.comment ? ` · ${t.comment}` : ''}
                        </span>
                      </li>
                    ))}
                    {txQuery.data?.length === 0 && !txQuery.isLoading && (
                      <li className="muted">Пока нет операций</li>
                    )}
                  </ul>
                </div>
                {(txQuery.data?.length ?? 0) >= moneyHistoryTake && (
                  <button
                    type="button"
                    className="ghost"
                    style={{ marginTop: 10 }}
                    disabled={txQuery.isFetching}
                    onClick={() => setMoneyHistoryTake((n) => n + CUSTOMER_HISTORY_PAGE)}
                  >
                    {txQuery.isFetching ? 'Загрузка…' : 'Загрузить ещё'}
                  </button>
                )}
              </div>
            )}

            {cardTab === 'time' && (
              <div>
                {canManageCustomers && (
                  <div className="customer-card-actions">
                    <button
                      type="button"
                      onClick={() => {
                        setError(null)
                        setTimeBankMinutes(30)
                        setTimeBankDirection('Credit')
                        setTimeBankComment('')
                        setTimeBankZoneId(zones[0]?.id ?? '')
                        setTimeBankOpen(true)
                      }}
                    >
                      Начислить / списать
                    </button>
                  </div>
                )}
                {zoneBanks.length > 0 ? (
                  <ul className="customer-zone-banks">
                    {zoneBanks.map((b) => (
                      <li key={b.zoneId}>
                        <span>{b.zoneName}</span>
                        <strong>{formatDuration(b.minutes)}</strong>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="muted">Банк по зонам пуст</p>
                )}
                <p className="muted" style={{ fontSize: 12 }}>
                  Минуты зоны Standard нельзя тратить в VIP и наоборот.
                </p>
                <h3 style={{ margin: '16px 0 10px', fontSize: '1rem' }}>История банка</h3>
                {timeBankTxQuery.isLoading && <p className="muted">Загрузка…</p>}
                <div className="customer-history-panel">
                  <ul className="customer-history-list">
                    {timeBankTxQuery.data?.map((t) => (
                      <li key={t.id}>
                        <div className="hist-main">
                          <strong>
                            {isCredit(t.direction) ? '+' : '−'}
                            {formatDuration(t.minutes)}
                            <span className="muted"> → {formatDuration(t.balanceAfter)}</span>
                          </strong>
                          <span className="muted">{new Date(t.createdAt).toLocaleString('ru-RU')}</span>
                        </div>
                        <span className="muted">
                          {t.zoneName ?? 'зона'} · {timeBankReasonLabel(t.reason)}
                          {t.comment ? ` · ${t.comment}` : ''}
                        </span>
                      </li>
                    ))}
                    {timeBankTxQuery.data?.length === 0 && !timeBankTxQuery.isLoading && (
                      <li className="muted">Пока нет операций</li>
                    )}
                  </ul>
                </div>
                {(timeBankTxQuery.data?.length ?? 0) >= timeHistoryTake && (
                  <button
                    type="button"
                    className="ghost"
                    style={{ marginTop: 10 }}
                    disabled={timeBankTxQuery.isFetching}
                    onClick={() => setTimeHistoryTake((n) => n + CUSTOMER_HISTORY_PAGE)}
                  >
                    {timeBankTxQuery.isFetching ? 'Загрузка…' : 'Загрузить ещё'}
                  </button>
                )}
              </div>
            )}

            {cardTab === 'case' && (
              <div>
                <div className="customer-stat" style={{ marginBottom: 14, maxWidth: 280 }}>
                  <span>Ключей SHIFT CASE</span>
                  <strong style={{ fontSize: '1.6rem' }}>{readCaseKeysBalance(detail)}</strong>
                </div>
                <div className="customer-card-actions">
                  <button type="button" onClick={() => void openCaseOnDisplay()}>
                    Открыть кейс
                  </button>
                  {canDeposit && (
                    <button
                      type="button"
                      onClick={() => {
                        setError(null)
                        setBuyKeysQty(1)
                        setBuyKeysPrice(CASE_KEY_DEFAULT_PRICE)
                        setBuyKeysOpen(true)
                      }}
                    >
                      Купить ключ
                    </button>
                  )}
                  {canManageCustomers && (
                    <button
                      type="button"
                      className="ghost"
                      onClick={() => {
                        setError(null)
                        setGrantKeysQty(1)
                        setGrantKeysComment('')
                        setGrantKeysOpen(true)
                      }}
                    >
                      Начислить ключ
                    </button>
                  )}
                  <a
                    className="btn ghost"
                    href="/site/case.html?desk=1"
                    target="_blank"
                    rel="noreferrer"
                    title="Экран рулетки на втором мониторе"
                  >
                    Экран кейса ↗
                  </a>
                </div>
                <p className="muted" style={{ fontSize: 13, marginTop: 12 }}>
                  «Открыть кейс» отправляет рулетку на экран акции. После спина приз покажем здесь — зачислите
                  вручную на кассе.
                </p>
                {deskWaitCmdId && (
                  <p className="flash" style={{ marginTop: 10 }}>
                    Ожидаем результат открытия…
                  </p>
                )}
              </div>
            )}

            {cardTab === 'access' && (
              <div className="form-stack">
                <p className="muted" style={{ margin: 0, fontSize: 13 }}>
                  Вход на Shell: телефон или логин + ПИН или пароль.
                  {detail.hasPin ? ' ПИН задан.' : ' ПИН не задан.'}
                  {detail.hasPassword ? ' Пароль задан.' : ' Пароль не задан.'}
                </p>
                <label>
                  Логин (опционально)
                  <input
                    value={credLogin}
                    onChange={(e) => setCredLogin(e.target.value)}
                    placeholder="вместо телефона"
                  />
                </label>
                <label>
                  Новый пароль
                  <input
                    type="password"
                    value={credPassword}
                    disabled={clearPassword}
                    onChange={(e) => setCredPassword(e.target.value)}
                    placeholder="мин. 4 символа · пусто = не менять"
                  />
                </label>
                <label className="check-row">
                  <input
                    type="checkbox"
                    checked={clearPassword}
                    onChange={(e) => {
                      setClearPassword(e.target.checked)
                      if (e.target.checked) setCredPassword('')
                    }}
                  />
                  Сбросить пароль
                </label>
                <label>
                  Новый ПИН
                  <input
                    type="password"
                    inputMode="numeric"
                    value={credPin}
                    disabled={clearPin}
                    onChange={(e) => setCredPin(e.target.value)}
                    placeholder="4+ цифр · пусто = не менять"
                  />
                </label>
                <label className="check-row">
                  <input
                    type="checkbox"
                    checked={clearPin}
                    onChange={(e) => {
                      setClearPin(e.target.checked)
                      if (e.target.checked) setCredPin('')
                    }}
                  />
                  Сбросить ПИН
                </label>
                <div className="customer-card-actions">
                  <button
                    type="button"
                    disabled={
                      credMutation.isPending ||
                      (!credLogin.trim() && !credPassword.trim() && !credPin.trim() && !clearPassword && !clearPin)
                    }
                    onClick={() => credMutation.mutate()}
                  >
                    {credMutation.isPending ? 'Сохранение…' : 'Сохранить доступ'}
                  </button>
                </div>
              </div>
            )}

            {cardTab === 'history' && (
              <div>
                <div className="customer-card-tabs" style={{ marginBottom: 12 }}>
                  <button
                    type="button"
                    className={historyTab === 'money' ? undefined : 'ghost'}
                    onClick={() => setHistoryTab('money')}
                  >
                    Деньги
                  </button>
                  <button
                    type="button"
                    className={historyTab === 'time' ? undefined : 'ghost'}
                    onClick={() => setHistoryTab('time')}
                  >
                    Банк минут
                  </button>
                </div>
                {historyTab === 'money' ? (
                  <>
                    <p className="muted" style={{ fontSize: 12, margin: '0 0 10px' }}>
                      Движения по балансу и бонусам. Оплата наличными / Kaspi — в «Касса → Чеки».
                    </p>
                    {txQuery.isLoading && <p className="muted">Загрузка…</p>}
                    <div className="customer-history-panel">
                      <ul className="customer-history-list">
                        {txQuery.data?.map((t) => (
                          <li key={t.id}>
                            <div className="hist-main">
                              <strong>
                                {isCredit(t.direction) ? '+' : '−'}
                                {t.amount.toLocaleString('ru-RU')} ₸
                              </strong>
                              <span className="muted">{new Date(t.createdAt).toLocaleString('ru-RU')}</span>
                            </div>
                            <span className="muted">
                              {ledgerTypeLabel(t.type)}
                              {t.comment ? ` · ${t.comment}` : ''}
                            </span>
                          </li>
                        ))}
                        {txQuery.data?.length === 0 && !txQuery.isLoading && (
                          <li className="muted">Пока нет операций</li>
                        )}
                      </ul>
                    </div>
                    {(txQuery.data?.length ?? 0) >= moneyHistoryTake && (
                      <button
                        type="button"
                        className="ghost"
                        style={{ marginTop: 10 }}
                        disabled={txQuery.isFetching}
                        onClick={() => setMoneyHistoryTake((n) => n + CUSTOMER_HISTORY_PAGE)}
                      >
                        {txQuery.isFetching ? 'Загрузка…' : 'Загрузить ещё'}
                      </button>
                    )}
                  </>
                ) : (
                  <>
                    {timeBankTxQuery.isLoading && <p className="muted">Загрузка…</p>}
                    <div className="customer-history-panel">
                      <ul className="customer-history-list">
                        {timeBankTxQuery.data?.map((t) => (
                          <li key={t.id}>
                            <div className="hist-main">
                              <strong>
                                {isCredit(t.direction) ? '+' : '−'}
                                {formatDuration(t.minutes)}
                                <span className="muted"> → {formatDuration(t.balanceAfter)}</span>
                              </strong>
                              <span className="muted">{new Date(t.createdAt).toLocaleString('ru-RU')}</span>
                            </div>
                            <span className="muted">
                              {t.zoneName ?? 'зона'} · {timeBankReasonLabel(t.reason)}
                              {t.comment ? ` · ${t.comment}` : ''}
                            </span>
                          </li>
                        ))}
                        {timeBankTxQuery.data?.length === 0 && !timeBankTxQuery.isLoading && (
                          <li className="muted">Пока нет операций</li>
                        )}
                      </ul>
                    </div>
                    {(timeBankTxQuery.data?.length ?? 0) >= timeHistoryTake && (
                      <button
                        type="button"
                        className="ghost"
                        style={{ marginTop: 10 }}
                        disabled={timeBankTxQuery.isFetching}
                        onClick={() => setTimeHistoryTake((n) => n + CUSTOMER_HISTORY_PAGE)}
                      >
                        {timeBankTxQuery.isFetching ? 'Загрузка…' : 'Загрузить ещё'}
                      </button>
                    )}
                  </>
                )}
              </div>
            )}
          </>
        )}
      </ActionDialog>

      <ActionDialog
        open={createOpen}
        onClose={() => !createMutation.isPending && setCreateOpen(false)}
        title="Новый клиент"
        size="md"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setCreateOpen(false)} disabled={createMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={
                createMutation.isPending ||
                !createForm.firstName.trim() ||
                createForm.phone.replace(/\D/g, '').length < 10
              }
              onClick={() => createMutation.mutate()}
            >
              Создать
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div className="field-grid">
            <label>
              Имя
              <input
                value={createForm.firstName}
                onChange={(e) => setCreateForm((f) => ({ ...f, firstName: e.target.value }))}
              />
            </label>
            <label>
              Фамилия
              <input
                value={createForm.lastName}
                onChange={(e) => setCreateForm((f) => ({ ...f, lastName: e.target.value }))}
                placeholder="необязательно"
              />
            </label>
          </div>
          <label>
            Телефон
            <input
              value={createForm.phone}
              onChange={(e) => setCreateForm((f) => ({ ...f, phone: e.target.value }))}
              placeholder="77001234567"
            />
          </label>
          <label>
            Email (необязательно)
            <input
              value={createForm.email}
              onChange={(e) => setCreateForm((f) => ({ ...f, email: e.target.value }))}
            />
          </label>
          <label>
            ПИН для входа на ПК (необязательно)
            <input
              value={createForm.pin}
              onChange={(e) => setCreateForm((f) => ({ ...f, pin: e.target.value }))}
              placeholder="4+ цифр — иначе клиент задаст сам при первом входе"
            />
          </label>
          <p className="muted" style={{ margin: 0, fontSize: 12 }}>
            Телефон — логин на Shell. Если ПИН не задать, при первом входе с ПК клиент сам поставит ПИН или пароль.
          </p>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={depositOpen}
        onClose={() => !depositMutation.isPending && setDepositOpen(false)}
        title="Пополнение баланса"
        description={detail?.fullName}
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setDepositOpen(false)} disabled={depositMutation.isPending}>
              Отмена
            </button>
            <button type="button" disabled={depositMutation.isPending || depositAmount < 100} onClick={() => depositMutation.mutate()}>
              Пополнить
              {depositQuote && depositQuote.totalBonusAmount > 0
                ? ` (+${depositQuote.totalBonusAmount.toLocaleString('ru-RU')} бон.)`
                : ''}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Сумма, ₸
            <input
              type="number"
              min={100}
              step={100}
              value={depositAmount}
              onChange={(e) => setDepositAmount(Number(e.target.value))}
            />
          </label>
          <label>
            Способ оплаты
            <select value={depositMethod} onChange={(e) => setDepositMethod(e.target.value as typeof depositMethod)}>
              <option value="Cash">Наличные</option>
              <option value="KaspiQr">Kaspi QR</option>
            </select>
          </label>
          {depositAmount >= 100 && (
            <div className="list-card" style={{ padding: 12, margin: 0 }}>
              {depositQuoteQuery.isFetching && !depositQuote && <p className="muted" style={{ margin: 0 }}>Считаем бонусы…</p>}
              {depositQuoteQuery.isError && (
                <p className="error" style={{ margin: 0 }}>
                  {(depositQuoteQuery.error as Error).message}
                </p>
              )}
              {depositQuote && (
                <>
                  <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, marginBottom: 6 }}>
                    <span className="muted">На основной баланс</span>
                    <strong>+{depositQuote.amount.toLocaleString('ru-RU')} ₸</strong>
                  </div>
                  {depositQuote.loyaltyBonusAmount > 0 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, marginBottom: 6 }}>
                      <span className="muted">Бонус уровня ({depositQuote.loyaltyBonusPercent}%)</span>
                      <strong className="chip-bonus" style={{ background: 'transparent', padding: 0 }}>
                        +{depositQuote.loyaltyBonusAmount.toLocaleString('ru-RU')} ₸
                      </strong>
                    </div>
                  )}
                  {depositQuote.tierBonusAmount > 0 && (
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, marginBottom: 6 }}>
                      <span className="muted">
                        Бонус за сумму
                        {depositQuote.tierMinAmount > 0
                          ? ` (от ${depositQuote.tierMinAmount.toLocaleString('ru-RU')} ₸)`
                          : ''}
                      </span>
                      <strong className="chip-bonus" style={{ background: 'transparent', padding: 0 }}>
                        +{depositQuote.tierBonusAmount.toLocaleString('ru-RU')} ₸
                      </strong>
                    </div>
                  )}
                  {depositQuote.depositBonusEnabled &&
                    depositQuote.tierBonusAmount <= 0 &&
                    depositQuote.loyaltyBonusAmount <= 0 && (
                      <p className="muted" style={{ margin: '0 0 6px', fontSize: 12 }}>
                        Для порогового бонуса увеличьте сумму пополнения.
                      </p>
                    )}
                  {!depositQuote.depositBonusEnabled &&
                    depositQuote.loyaltyBonusAmount <= 0 && (
                      <p className="muted" style={{ margin: '0 0 6px', fontSize: 12 }}>
                        Бонусы за пополнение сейчас не начисляются.
                      </p>
                    )}
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      gap: 12,
                      marginTop: 8,
                      paddingTop: 8,
                      borderTop: '1px solid var(--border, #333)',
                    }}
                  >
                    <span>Итого бонусов</span>
                    <strong>+{depositQuote.totalBonusAmount.toLocaleString('ru-RU')} ₸</strong>
                  </div>
                  <p className="muted" style={{ margin: '10px 0 0', fontSize: 12 }}>
                    После: баланс {depositQuote.balanceAfterDeposit.toLocaleString('ru-RU')} ₸ · бонусы{' '}
                    {depositQuote.bonusBalanceAfterDeposit.toLocaleString('ru-RU')} ₸
                  </p>
                </>
              )}
            </div>
          )}
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={casePrizeOpen && !!casePrize}
        onClose={() => !caseApplyMutation.isPending && setCasePrizeOpen(false)}
        title="Приз SHIFT CASE"
        description={casePrize?.displayName || detail?.fullName}
        size="sm"
        footer={
          <>
            {casePrize?.rewardId &&
            casePrize.rewardStatus === CaseRewardStatus.Pending &&
            canManageCustomers ? (
              <button
                type="button"
                disabled={
                  caseApplyMutation.isPending ||
                  (casePrizeNeedsZone(casePrize.prizeType, casePrize.payloadJson) && !casePrizeZoneId)
                }
                onClick={() => caseApplyMutation.mutate()}
              >
                {caseApplyMutation.isPending
                  ? 'Зачисляем…'
                  : caseApplyButtonLabel(casePrize.prizeType, casePrize.payloadJson)}
              </button>
            ) : null}
            <button
              type="button"
              className={casePrize?.rewardStatus === CaseRewardStatus.Pending ? 'ghost' : undefined}
              disabled={caseApplyMutation.isPending}
              onClick={() => setCasePrizeOpen(false)}
            >
              {casePrize?.rewardStatus === CaseRewardStatus.Pending ? 'Позже' : 'Понятно'}
            </button>
          </>
        }
      >
        {casePrize && (
          <div className="form-stack" style={{ alignItems: 'stretch', textAlign: 'center' }}>
            {casePrize.imageUrl ? (
              <img
                src={casePrize.imageUrl}
                alt=""
                style={{
                  width: 120,
                  height: 120,
                  objectFit: 'contain',
                  borderRadius: 12,
                  alignSelf: 'center',
                }}
              />
            ) : null}
            <strong style={{ fontSize: '1.25rem' }}>{casePrize.prizeName}</strong>
            {casePrize.rewardStatus === CaseRewardStatus.Pending ? (
              <div
                className="tg-banner is-ok"
                style={{ textAlign: 'left', margin: '4px 0 0', fontSize: 13, lineHeight: 1.45 }}
              >
                {casePrize.applyMessage ||
                  'Кассир: зачислите приз вручную — автоматически ничего не начисляется.'}
              </div>
            ) : casePrizeApplyMsg || casePrize.applyMessage ? (
              <p className="flash" style={{ margin: 0, textAlign: 'left' }}>
                {casePrizeApplyMsg || casePrize.applyMessage}
              </p>
            ) : null}
            {casePrize.rewardStatus === CaseRewardStatus.Pending &&
            casePrizeNeedsZone(casePrize.prizeType, casePrize.payloadJson) ? (
              <label style={{ textAlign: 'left', marginTop: 8 }}>
                <span className="muted" style={{ fontSize: 13 }}>
                  Зал для начисления времени
                </span>
                <select
                  value={casePrizeZoneId}
                  onChange={(e) => setCasePrizeZoneId(e.target.value)}
                  style={{ width: '100%', marginTop: 6 }}
                >
                  <option value="">— выберите зал —</option>
                  {zones.map((z) => (
                    <option key={z.id} value={z.id}>
                      {z.name}
                    </option>
                  ))}
                </select>
              </label>
            ) : null}
            {casePrize.keysRemaining != null ? (
              <p className="muted" style={{ margin: 0, fontSize: 13 }}>
                Ключей осталось: {casePrize.keysRemaining}
              </p>
            ) : null}
            {error && casePrizeOpen ? (
              <p className="error" style={{ margin: 0, textAlign: 'left' }}>
                {error}
              </p>
            ) : null}
          </div>
        )}
      </ActionDialog>

      <ActionDialog
        open={buyKeysOpen}
        onClose={() => !buyKeysMutation.isPending && setBuyKeysOpen(false)}
        title="Купить ключ SHIFT CASE"
        description={detail?.fullName}
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setBuyKeysOpen(false)} disabled={buyKeysMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={buyKeysMutation.isPending || buyKeysQty < 1 || buyKeysPrice < 100}
              onClick={() => buyKeysMutation.mutate()}
            >
              {buyKeysMutation.isPending
                ? 'Продаём…'
                : `Оплатить ${(buyKeysQty * buyKeysPrice).toLocaleString('ru-RU')} ₸`}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <div>
            <span className="muted" style={{ fontSize: 13 }}>
              Количество
            </span>
            <div className="action-chip-row" style={{ marginTop: 6 }}>
              {[1, 2, 3, 5].map((n) => (
                <button
                  key={n}
                  type="button"
                  className={buyKeysQty === n ? undefined : 'ghost'}
                  onClick={() => setBuyKeysQty(n)}
                >
                  {n}
                </button>
              ))}
            </div>
          </div>
          <div>
            <span className="muted" style={{ fontSize: 13 }}>
              Цена за ключ
            </span>
            <div className="action-chip-row" style={{ marginTop: 6 }}>
              {[500, 1000, 1500, 2000].map((p) => (
                <button
                  key={p}
                  type="button"
                  className={buyKeysPrice === p ? undefined : 'ghost'}
                  onClick={() => setBuyKeysPrice(p)}
                >
                  {p.toLocaleString('ru-RU')} ₸
                </button>
              ))}
            </div>
          </div>
          <label>
            Оплата
            <select value={buyKeysMethod} onChange={(e) => setBuyKeysMethod(e.target.value as typeof buyKeysMethod)}>
              <option value="Cash">Наличные</option>
              <option value="KaspiQr">Kaspi QR</option>
              <option value="Card">Карта</option>
            </select>
          </label>
          <p style={{ margin: 0, fontWeight: 600 }}>
            Итого: {(buyKeysQty * buyKeysPrice).toLocaleString('ru-RU')} ₸
          </p>
          <p className="muted" style={{ margin: 0, fontSize: 12 }}>
            Создаётся кассовый чек «Ключ SHIFT CASE» — попадает в выручку и отчёты.
          </p>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={grantKeysOpen}
        onClose={() => !grantKeysMutation.isPending && setGrantKeysOpen(false)}
        title="Начислить ключ CASE"
        description={detail?.fullName}
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setGrantKeysOpen(false)} disabled={grantKeysMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={grantKeysMutation.isPending || grantKeysQty < 1 || grantKeysComment.trim().length < 3}
              onClick={() => grantKeysMutation.mutate()}
            >
              {grantKeysMutation.isPending ? 'Начисляем…' : 'Начислить'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Ключей
            <input
              type="number"
              min={1}
              max={20}
              value={grantKeysQty}
              onChange={(e) => setGrantKeysQty(Math.max(1, Number(e.target.value) || 1))}
            />
          </label>
          <label>
            Комментарий
            <input
              value={grantKeysComment}
              onChange={(e) => setGrantKeysComment(e.target.value)}
              placeholder="Подарок / компенсация…"
            />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={adjustOpen}
        onClose={() => !adjustMutation.isPending && setAdjustOpen(false)}
        title="Корректировка баланса"
        description={detail?.fullName}
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setAdjustOpen(false)} disabled={adjustMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={adjustMutation.isPending || adjustAmount < 1 || !adjustComment.trim()}
              onClick={() => adjustMutation.mutate()}
            >
              Провести
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Операция
            <select value={adjustDirection} onChange={(e) => setAdjustDirection(e.target.value as typeof adjustDirection)}>
              <option value="Credit">Начислить</option>
              <option value="Debit">Списать</option>
            </select>
          </label>
          <label>
            Сумма, ₸
            <input type="number" min={1} value={adjustAmount} onChange={(e) => setAdjustAmount(Number(e.target.value))} />
          </label>
          <label>
            Комментарий
            <input value={adjustComment} onChange={(e) => setAdjustComment(e.target.value)} placeholder="Причина" />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>

      <ActionDialog
        open={timeBankOpen}
        onClose={() => !timeBankMutation.isPending && setTimeBankOpen(false)}
        title="Банк времени"
        description={detail ? `${detail.fullName} · всего ${formatDuration(bankMinutes)}` : undefined}
        size="sm"
        footer={
          <>
            <button type="button" className="ghost" onClick={() => setTimeBankOpen(false)} disabled={timeBankMutation.isPending}>
              Отмена
            </button>
            <button
              type="button"
              disabled={
                timeBankMutation.isPending ||
                !timeBankZoneId ||
                timeBankMinutes < 1 ||
                !timeBankComment.trim() ||
                (timeBankDirection === 'Debit' && timeBankMinutes > selectedZoneBank)
              }
              onClick={() => timeBankMutation.mutate()}
            >
              {timeBankDirection === 'Credit' ? 'Начислить' : 'Списать'}
            </button>
          </>
        }
      >
        <div className="form-stack">
          <label>
            Зона
            <select value={timeBankZoneId} onChange={(e) => setTimeBankZoneId(e.target.value)}>
              <option value="">Выберите зону</option>
              {zones.map((z) => (
                <option key={z.id} value={z.id}>
                  {z.name}
                  {zoneBanks.find((b) => b.zoneId === z.id)
                    ? ` · банк ${formatDuration(zoneBanks.find((b) => b.zoneId === z.id)!.minutes)}`
                    : ''}
                </option>
              ))}
            </select>
          </label>
          <label>
            Операция
            <select
              value={timeBankDirection}
              onChange={(e) => setTimeBankDirection(e.target.value as 'Credit' | 'Debit')}
            >
              <option value="Credit">Начислить минуты</option>
              <option value="Debit">Списать минуты</option>
            </select>
          </label>
          <label>
            Минут
            <input
              type="number"
              min={1}
              step={1}
              value={timeBankMinutes}
              onChange={(e) => setTimeBankMinutes(Number(e.target.value))}
            />
          </label>
          <label>
            Комментарий
            <input
              value={timeBankComment}
              onChange={(e) => setTimeBankComment(e.target.value)}
              placeholder="Причина"
            />
          </label>
          {error && <p className="error">{error}</p>}
        </div>
      </ActionDialog>
    </>
  )
}
