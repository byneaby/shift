export type Rarity = 'common' | 'uncommon' | 'rare' | 'epic' | 'legendary'
export type PrizeType = 'bonus' | 'time' | 'bar' | 'custom'
export type OpenMode = 'public' | 'ticket'
export type SpinStatus = 'open' | 'credited' | 'void'

export type PublicPrize = {
  id: string
  title: string
  detail: string
  rarity: Rarity
  prizeType: PrizeType
  image: string | null
  odds?: number
}

export type PublicCase = {
  club: { slug: string; name: string; city: string; tagline: string; accent: string }
  case: {
    slug: string
    title: string
    lead: string
    openMode: OpenMode
    showOdds: boolean
    prizes: PublicPrize[]
  }
}

export type ClubPublic = {
  slug: string
  name: string
  city: string
  tagline: string
  accent: string
  cases: { slug: string; title: string; lead: string }[]
}

export type Me = {
  id: string
  email: string
  name: string
  role: 'admin' | 'club'
  clubRole: 'owner' | 'staff' | null
  club: { slug: string; name: string } | null
}

export type StaffPrize = {
  id: string
  title: string
  detail: string
  rarity: Rarity
  prizeType: PrizeType
  weight: number
  cashierHint: string
  image: string | null
  dailyLimit: number | null
  totalLimit: number | null
  sortOrder: number
  active: boolean
}

export type ClubCase = {
  id: string
  slug: string
  title: string
  lead: string
  openMode: OpenMode
  cooldownSeconds: number
  showOdds: boolean
  enabled: boolean
  prizes: StaffPrize[]
}

export type DeskSpin = {
  id: string
  code: string
  title: string
  detail: string
  rarity: Rarity
  prizeType: PrizeType
  cashierHint: string
  image: string | null
  status: SpinStatus
  createdAt: string
  creditedAt: string | null
  note: string | null
}

export type ClubDesk = {
  id: string
  slug: string
  name: string
  city: string
  tagline: string
  accent: string
  role: 'owner' | 'staff'
  today: number
  waiting: number
  creditedToday: number
}

export type StaffUser = {
  id: string
  name: string
  email: string
  clubRole: 'owner' | 'staff'
  active: boolean
  createdAt: string
}

export type AdminClub = {
  id: string
  slug: string
  name: string
  city: string
  tagline: string
  active: boolean
  createdAt: string
  owner: { name: string; email: string } | null
  spins: number
  waiting: number
}

export type Ticket = { code: string; createdAt: string; usedAt: string | null }

export type SpinResult = {
  code: string
  landing: number
  prize: PublicPrize
  strip: PublicPrize[]
}
