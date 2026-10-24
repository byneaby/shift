import type { PrizeType, Rarity, SpinStatus } from './types'

export const rarityLabel: Record<Rarity, string> = {
  common: 'Обычный',
  uncommon: 'Необычный',
  rare: 'Редкий',
  epic: 'Эпический',
  legendary: 'Легендарный',
}

export const typeLabel: Record<PrizeType, string> = {
  bonus: 'Бонус',
  time: 'Время',
  bar: 'Бар',
  custom: 'Свой приз',
}

export const statusLabel: Record<SpinStatus, string> = {
  open: 'Ждёт зачисления',
  credited: 'Зачислено',
  void: 'Скрыто',
}

export function when(iso: string) {
  return new Intl.DateTimeFormat('ru-RU', {
    timeZone: 'Asia/Almaty',
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: 'short',
  }).format(new Date(iso))
}
