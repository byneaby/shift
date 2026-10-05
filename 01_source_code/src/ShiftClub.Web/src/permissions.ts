import type { EmployeeDto } from './api/client'

export const Perm = {
  BarView: 'bar.view',
  BarSell: 'bar.sell',
  BarOrders: 'bar.orders',
  InventoryManage: 'inventory.manage',
  ComputersView: 'computers.view',
  ComputersManage: 'computers.manage',
  ComputersCommand: 'computers.command',
  TariffsManage: 'tariffs.manage',
  ZonesView: 'zones.view',
  ZonesManage: 'zones.manage',
  EmployeesView: 'employees.view',
  EmployeesManage: 'employees.manage',
  AuditView: 'audit.view',
  SettingsManage: 'settings.manage',
  ReportsView: 'reports.view',
  CashView: 'cash.view',
  CustomersView: 'customers.view',
  CustomersManage: 'customers.manage',
  CustomersDeposit: 'customers.deposit',
  CustomersAdjust: 'customers.adjust',
  BookingsView: 'bookings.view',
} as const

export type PermissionCode = (typeof Perm)[keyof typeof Perm] | string

export function getEmployee(): EmployeeDto | null {
  try {
    return JSON.parse(localStorage.getItem('shiftclub.employee') ?? 'null') as EmployeeDto | null
  } catch {
    return null
  }
}

export function isOwner(employee = getEmployee()): boolean {
  return (employee?.roles ?? []).some((r: string) => r.toLowerCase() === 'owner')
}

/** True if employee has any of the codes, or is owner. */
export function can(...codes: PermissionCode[]): boolean {
  const employee = getEmployee()
  if (!employee) return false
  if (isOwner(employee)) return true
  if (codes.length === 0) return true
  const set = new Set(employee.permissions ?? [])
  return codes.some((c) => set.has(c))
}
