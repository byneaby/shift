export type EnumValue = string | number | null | undefined

function enumName(value: EnumValue, names: readonly string[]) {
  if (typeof value === 'number') return names[value]
  if (typeof value !== 'string') return undefined
  if (/^\d+$/.test(value)) return names[Number(value)]
  return value
}

export function money(value: number) {
  return value.toLocaleString('ru-RU', { maximumFractionDigits: 2 })
}

/** До часа — минуты; от часа — часы и остаток минут. */
export function formatDuration(minutes: number) {
  const rounded = Math.max(0, Math.round(minutes))
  if (rounded < 60) return `${rounded} мин`
  const hours = Math.floor(rounded / 60)
  const rest = rounded % 60
  return rest ? `${hours} ч ${rest} мин` : `${hours} ч`
}

const PAYMENT_METHODS = ['Cash', 'Card', 'Mixed', 'Balance', 'Free', 'Postpay', 'KaspiQr', 'Transfer'] as const
const PAYMENT_LABELS: Record<string, string> = {
  Cash: 'Наличные',
  Card: 'Карта',
  Mixed: 'Смешанная оплата',
  Balance: 'Баланс клиента',
  Free: 'Без оплаты',
  Postpay: 'Постоплата',
  KaspiQr: 'Kaspi QR',
  Transfer: 'Перевод',
}

export function paymentMethodName(value: EnumValue) {
  return enumName(value, PAYMENT_METHODS)
}

export function paymentMethodLabel(value: EnumValue) {
  const name = paymentMethodName(value)
  return (name && PAYMENT_LABELS[name]) || 'Не указан'
}

const RECEIPT_STATUSES = ['Draft', 'Paid', 'Cancelled', 'Refunded', 'PartiallyRefunded'] as const
const RECEIPT_STATUS_LABELS: Record<string, string> = {
  Draft: 'Черновик',
  Paid: 'Оплачен',
  Cancelled: 'Отменён',
  Refunded: 'Возвращён',
  PartiallyRefunded: 'Частичный возврат',
}

export function receiptStatusName(value: EnumValue) {
  return enumName(value, RECEIPT_STATUSES)
}

export function receiptStatusLabel(value: EnumValue) {
  const name = receiptStatusName(value)
  return (name && RECEIPT_STATUS_LABELS[name]) || 'Неизвестный статус'
}

export function isReceiptRefunded(value: EnumValue) {
  const name = receiptStatusName(value)
  return name === 'Refunded' || name === 'PartiallyRefunded'
}

const BOOKING_STATUSES = ['Pending', 'Confirmed', 'Arrived', 'Active', 'Completed', 'Cancelled', 'NoShow'] as const
const BOOKING_STATUS_LABELS: Record<string, string> = {
  Pending: 'Ожидает подтверждения',
  Confirmed: 'Подтверждена',
  Arrived: 'Клиент пришёл',
  Active: 'Сеанс запущен',
  Completed: 'Завершена',
  Cancelled: 'Отменена',
  NoShow: 'Неявка',
}

export function bookingStatusName(value: EnumValue) {
  return enumName(value, BOOKING_STATUSES)
}

export function bookingStatusLabel(value: EnumValue) {
  const name = bookingStatusName(value)
  return (name && BOOKING_STATUS_LABELS[name]) || 'Неизвестный статус'
}

export function isBookingStatus(value: EnumValue, ...names: string[]) {
  return names.includes(bookingStatusName(value) ?? '')
}

export function isOpenBookingStatus(value: EnumValue) {
  return ['Pending', 'Confirmed', 'Arrived', 'Active'].includes(bookingStatusName(value) ?? '')
}

const SESSION_STATUSES = [
  'Draft',
  'Reserved',
  'Waiting',
  'Active',
  'Paused',
  'Completed',
  'Cancelled',
  'Interrupted',
  'PaymentPending',
] as const

export function sessionStatusName(value: EnumValue) {
  return enumName(value, SESSION_STATUSES)
}

export function isSessionStatus(value: EnumValue, ...names: string[]) {
  return names.includes(sessionStatusName(value) ?? '')
}

const CASH_SHIFT_STATUSES = ['Open', 'Closing', 'Closed', 'ForceClosed'] as const
const CASH_SHIFT_STATUS_LABELS: Record<string, string> = {
  Open: 'Открыта',
  Closing: 'Закрывается',
  Closed: 'Закрыта',
  ForceClosed: 'Закрыта принудительно',
}

export function cashShiftStatusLabel(value: EnumValue) {
  const name = enumName(value, CASH_SHIFT_STATUSES)
  return (name && CASH_SHIFT_STATUS_LABELS[name]) || 'Неизвестный статус'
}

export function isCashShiftOpen(value: EnumValue) {
  return enumName(value, CASH_SHIFT_STATUSES) === 'Open'
}

const CASH_MOVEMENT_LABELS: Record<string, string> = {
  CashIn: 'Внесение в ящик',
  CashOut: 'Изъятие (инкассация)',
  Expense: 'Расход клуба',
}

export function cashMovementTypeLabel(value: EnumValue) {
  const name = typeof value === 'string' ? value : String(value ?? '')
  return CASH_MOVEMENT_LABELS[name] || name || '—'
}

const WORK_SHIFT_STATUSES = ['Scheduled', 'Working', 'OnBreak', 'Completed', 'Absent', 'Late', 'Cancelled'] as const
const WORK_SHIFT_LABELS: Record<string, string> = {
  Scheduled: 'Запланирована',
  Working: 'На смене',
  OnBreak: 'Перерыв',
  Completed: 'Завершена',
  Absent: 'Неявка',
  Late: 'Опоздание',
  Cancelled: 'Отменена',
}

export function workShiftStatusName(value: EnumValue) {
  return enumName(value, WORK_SHIFT_STATUSES)
}

export function workShiftStatusLabel(value: EnumValue) {
  const name = workShiftStatusName(value)
  return (name && WORK_SHIFT_LABELS[name]) || 'Неизвестный статус'
}

const EMPLOYEE_PAY_TYPES = ['Hourly', 'FixedMonthly', 'PerShift'] as const
const EMPLOYEE_PAY_TYPE_LABELS: Record<string, string> = {
  Hourly: 'Почасовая оплата',
  FixedMonthly: 'Месячный оклад',
  PerShift: 'Оплата за смену',
}

export function employeePayTypeLabel(value: EnumValue) {
  const name = enumName(value, EMPLOYEE_PAY_TYPES)
  return (name && EMPLOYEE_PAY_TYPE_LABELS[name]) || 'Оплата не указана'
}

const PAYROLL_STATUSES = ['Draft', 'Approved', 'Paid', 'Cancelled'] as const
const PAYROLL_STATUS_LABELS: Record<string, string> = {
  Draft: 'Черновик',
  Approved: 'Утверждено',
  Paid: 'Выплачено',
  Cancelled: 'Отменено',
}

export function payrollStatusName(value: EnumValue) {
  return enumName(value, PAYROLL_STATUSES)
}

export function payrollStatusLabel(value: EnumValue) {
  const name = payrollStatusName(value)
  return (name && PAYROLL_STATUS_LABELS[name]) || 'Неизвестный статус'
}

const PAYROLL_TYPES = ['Salary', 'Hourly', 'ShiftPay', 'SalesPercent', 'Bonus', 'Penalty', 'Advance', 'Deduction', 'Premium'] as const
const PAYROLL_TYPE_LABELS: Record<string, string> = {
  Salary: 'Оклад',
  Hourly: 'Почасовая оплата',
  ShiftPay: 'Оплата смены',
  SalesPercent: 'Процент с продаж',
  Bonus: 'Бонус',
  Penalty: 'Штраф',
  Advance: 'Аванс',
  Deduction: 'Удержание',
  Premium: 'Премия',
}

export function payrollTypeLabel(value: EnumValue) {
  const name = enumName(value, PAYROLL_TYPES)
  return (name && PAYROLL_TYPE_LABELS[name]) || 'Прочее начисление'
}

const LEDGER_TYPES = ['Deposit', 'SessionCharge', 'ProductPurchase', 'Refund', 'BonusCredit', 'BonusDebit', 'ManualAdjustment', 'DebtPayment', 'Transfer'] as const
const LEDGER_TYPE_LABELS: Record<string, string> = {
  Deposit: 'Пополнение баланса',
  SessionCharge: 'Оплата сеанса',
  ProductPurchase: 'Покупка товара',
  Refund: 'Возврат',
  BonusCredit: 'Начисление бонусов',
  BonusDebit: 'Списание бонусов',
  ManualAdjustment: 'Ручная корректировка',
  DebtPayment: 'Оплата долга',
  Transfer: 'Перевод',
}

export function ledgerTypeLabel(value: EnumValue) {
  const name = enumName(value, LEDGER_TYPES)
  return (name && LEDGER_TYPE_LABELS[name]) || 'Операция'
}

const LEDGER_DIRECTIONS = ['Credit', 'Debit'] as const
export function isCredit(value: EnumValue) {
  return enumName(value, LEDGER_DIRECTIONS) === 'Credit'
}

const TIME_BANK_REASONS = ['SessionSaved', 'SessionSpent', 'ManualCredit', 'ManualDebit'] as const
const TIME_BANK_REASON_LABELS: Record<string, string> = {
  SessionSaved: 'Сохранено с сеанса',
  SessionSpent: 'Списано на сеанс',
  ManualCredit: 'Начислено вручную',
  ManualDebit: 'Списано вручную',
}

export function timeBankReasonLabel(value: EnumValue) {
  const name = enumName(value, TIME_BANK_REASONS)
  return (name && TIME_BANK_REASON_LABELS[name]) || 'Операция с временем'
}

const ITEM_TYPE_LABELS: Record<string, string> = {
  GamingTime: 'Игровое время',
  Package: 'Пакет',
  Product: 'Товар',
  BalanceTopUp: 'Пополнение баланса',
  Other: 'Прочее',
  BookingDeposit: 'Предоплата брони',
  CaseKey: 'Ключ кейса',
}

export function receiptItemTypeLabel(value: string) {
  return ITEM_TYPE_LABELS[value] ?? value
}

const BAR_ORDER_STATUSES = [
  'New',
  'Accepted',
  'Preparing',
  'Ready',
  'Delivering',
  'Completed',
  'Cancelled',
  'Rejected',
] as const
const BAR_ORDER_STATUS_LABELS: Record<string, string> = {
  New: 'Новый',
  Accepted: 'Принят',
  Preparing: 'Готовится',
  Ready: 'Готов',
  Delivering: 'В пути',
  Completed: 'Выдан',
  Cancelled: 'Отменён',
  Rejected: 'Отклонён',
}

export function barOrderStatusLabel(value: EnumValue) {
  const name = enumName(value, BAR_ORDER_STATUSES)
  return (name && BAR_ORDER_STATUS_LABELS[name]) || 'Статус заказа'
}

const BAR_PAYMENT_MODES = [
  'CashOnDelivery',
  'CardOnDelivery',
  'PayAtCashier',
  'ChargeToSession',
  'Balance',
  'KaspiQrOnDelivery',
] as const
const BAR_PAYMENT_MODE_LABELS: Record<string, string> = {
  CashOnDelivery: 'Наличные',
  CardOnDelivery: 'Карта',
  PayAtCashier: 'У кассы',
  ChargeToSession: 'К сеансу',
  Balance: 'С баланса',
  KaspiQrOnDelivery: 'Kaspi QR',
}

export function barOrderPaymentModeLabel(value: EnumValue) {
  const name = enumName(value, BAR_PAYMENT_MODES)
  return (name && BAR_PAYMENT_MODE_LABELS[name]) || 'Оплата'
}

/** Заказ оплачивается при выдаче на кассе (наличные / Kaspi QR / карта). */
export function isBarPaidOnDelivery(value: EnumValue) {
  const name = barOrderPaymentModeName(value)
  return (
    name === 'PayAtCashier' ||
    name === 'CashOnDelivery' ||
    name === 'KaspiQrOnDelivery' ||
    name === 'CardOnDelivery'
  )
}

function barOrderPaymentModeName(value: EnumValue) {
  return enumName(value, BAR_PAYMENT_MODES)
}

export function activeFlagLabel(isActive: boolean) {
  return isActive ? 'Активен' : 'Выкл'
}

const NEWS_CATEGORIES: Record<string, string> = {
  Info: 'Инфо',
  Promo: 'Акция',
  Event: 'Событие',
  Maintenance: 'Техработы',
}

export function newsCategoryLabel(value: string | null | undefined) {
  if (!value) return 'Новость'
  const key = value.trim()
  const normalized = key.charAt(0).toUpperCase() + key.slice(1).toLowerCase()
  return NEWS_CATEGORIES[key] ?? NEWS_CATEGORIES[normalized] ?? key
}

const ROLE_LABELS: Record<string, string> = {
  owner: 'Владелец',
  admin: 'Администратор',
  cashier: 'Кассир',
  manager: 'Менеджер',
  staff: 'Сотрудник',
}

export function roleLabel(value: string | null | undefined) {
  if (!value) return 'без роли'
  const key = value.trim().toLowerCase()
  return ROLE_LABELS[key] ?? value
}

export function rolesLabel(roles: string[] | null | undefined) {
  if (!roles?.length) return 'без ролей'
  return roles.map(roleLabel).join(', ')
}

const SESSION_STATUS_LABELS: Record<string, string> = {
  Draft: 'Черновик',
  Reserved: 'Резерв',
  Waiting: 'Ожидание',
  Active: 'Активен',
  Paused: 'Пауза',
  Completed: 'Завершён',
  Cancelled: 'Отменён',
  Interrupted: 'Прерван',
  PaymentPending: 'Ожидает оплаты',
}

export function sessionStatusLabel(value: EnumValue) {
  const name = sessionStatusName(value)
  return (name && SESSION_STATUS_LABELS[name]) || 'Сеанс'
}

/** Нормализует старые сохранённые названия чеков: «2+1 · 180 мин» → «2+1 · 3 ч». */
export function formatReceiptItemName(value: string) {
  let result = value.replace(/(\d+)\s*мин\b/gi, (_, raw: string) => formatDuration(Number(raw)))
  if (
    !result.startsWith('Игровое время') &&
    !result.startsWith('Продление') &&
    /(?:\d+\s*ч(?:\s*\d+\s*мин)?|\d+\s*мин)(?:\s*\(|$)/i.test(result)
  ) {
    result = `Игровое время · ${result}`
  }
  return result.replace(
    / · (\d+\s*ч(?:\s*\d+\s*мин)?|\d+\s*мин)(?=\s*\(|$)/i,
    ' · время: $1',
  )
}

const AUDIT_ACTION_LABELS: Record<string, string> = {
  'auth.login': 'Вход в панель',
  'auth.login.telegram': 'Вход через Telegram',
  'employee.create': 'Создан сотрудник',
  'employee.update': 'Изменён сотрудник',
  'employee.password.reset': 'Сброс пароля',
  'auth.password.change': 'Смена своего пароля',
  'employee.delete': 'Удалён сотрудник',
  'cash.shift.open': 'Открыта кассовая смена',
  'cash.shift.close': 'Закрыта кассовая смена',
  'cash.shift.force_close': 'Принудительно закрыта смена',
  'cash.receipt.create': 'Чек / продажа',
  'cash.receipt.create.owner_proxy': 'Чек владельца (в смене кассира)',
  'cash.receipt.refund': 'Возврат',
  'session.start': 'Старт сеанса',
  'session.start.owner_proxy': 'Старт сеанса (владелец)',
  'session.end': 'Конец сеанса',
  'session.transfer': 'Перенос сеанса',
  'session.complimentary': 'Комплиментарный сеанс',
  'booking.create': 'Создана бронь',
  'booking.delete': 'Удалена бронь',
  'customer.create': 'Создан клиент',
  'computer.approve': 'ПК одобрен',
  'computer.delete': 'ПК удалён',
  'computer.revoke': 'ПК отозван',
  'computer.command': 'Команда на ПК',
  'computer.wake': 'Wake-on-LAN',
}

export function auditActionLabel(action: string) {
  if (!action) return 'Действие'
  if (AUDIT_ACTION_LABELS[action]) return AUDIT_ACTION_LABELS[action]
  const prefix = action.split('.')[0]
  const group: Record<string, string> = {
    auth: 'Авторизация',
    employee: 'Сотрудники',
    cash: 'Касса',
    session: 'Сеанс',
    booking: 'Бронь',
    customer: 'Клиент',
    computer: 'ПК',
    bar: 'Бар',
  }
  return group[prefix] ? `${group[prefix]} · ${action}` : action
}

export function auditActionTone(action: string): 'ok' | 'warn' | 'bad' | 'neutral' {
  if (action.includes('delete') || action.includes('refund') || action.includes('revoke')) return 'bad'
  if (action.includes('password') || action.includes('force') || action.includes('proxy')) return 'warn'
  if (action.startsWith('auth.') || action.includes('create') || action.includes('start') || action.includes('open'))
    return 'ok'
  return 'neutral'
}

