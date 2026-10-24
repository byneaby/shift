import express from 'express'
import { createHash, createHmac, randomBytes, randomInt, scryptSync, timingSafeEqual } from 'node:crypto'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { db, dataDir, hashPassword, root } from './db.js'

const app = express()
const port = Number(process.env.PORT || 8787)
const secretPath = join(dataDir, 'secret')
const secret = existsSync(secretPath) ? readFileSync(secretPath, 'utf8') : randomBytes(32).toString('hex')
if (!existsSync(secretPath)) writeFileSync(secretPath, secret, { mode: 0o600 })

const uploadDir = join(dataDir, 'uploads')
mkdirSync(uploadDir, { recursive: true })

app.disable('x-powered-by')
app.use(express.json({ limit: '2mb' }))
app.use((req, res, next) => {
  res.setHeader('X-Content-Type-Options', 'nosniff')
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin')
  res.setHeader('X-Frame-Options', 'SAMEORIGIN')
  next()
})
app.use('/uploads', express.static(uploadDir, { maxAge: '7d' }))

const wrap = (fn) => (req, res, next) => Promise.resolve(fn(req, res, next)).catch(next)
const ok = (res, data) => res.json({ ok: true, data })
const fail = (res, status, error) => res.status(status).json({ ok: false, error })

function sign(payload) {
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url')
  const sig = createHmac('sha256', secret).update(body).digest('base64url')
  return `${body}.${sig}`
}

function verify(token) {
  if (!token || !token.includes('.')) return null
  const [body, sig] = token.split('.')
  const expected = createHmac('sha256', secret).update(body).digest('base64url')
  const a = Buffer.from(sig)
  const b = Buffer.from(expected)
  if (a.length !== b.length || !timingSafeEqual(a, b)) return null
  try {
    const payload = JSON.parse(Buffer.from(body, 'base64url').toString())
    if (!payload.exp || payload.exp < Date.now()) return null
    return payload
  } catch {
    return null
  }
}

function readToken(req) {
  const header = req.get('authorization') || ''
  if (header.startsWith('Bearer ')) return header.slice(7)
  const raw = req.headers.cookie || ''
  const match = raw.match(/(?:^|; )clutch=([^;]+)/)
  return match ? decodeURIComponent(match[1]) : null
}

function setSession(res, userId) {
  const token = sign({ uid: userId, exp: Date.now() + 14 * 24 * 3600 * 1000 })
  res.setHeader(
    'Set-Cookie',
    `clutch=${encodeURIComponent(token)}; HttpOnly; Path=/; Max-Age=${14 * 24 * 3600}; SameSite=Lax`,
  )
}

function clearSession(res) {
  res.setHeader('Set-Cookie', 'clutch=; HttpOnly; Path=/; Max-Age=0; SameSite=Lax')
}

function userById(id) {
  return db.prepare('SELECT * FROM users WHERE id = ?').get(id)
}

function auth(req, res, next) {
  const payload = verify(readToken(req))
  const user = payload ? userById(payload.uid) : null
  if (!user || !user.active) return fail(res, 401, 'Нужно войти')
  req.user = user
  next()
}

function adminOnly(req, res, next) {
  if (req.user.role !== 'admin') return fail(res, 403, 'Это раздел платформы')
  next()
}

function clubOnly(req, res, next) {
  if (req.user.role !== 'club' || !req.user.club_id) return fail(res, 403, 'Это раздел клуба')
  const club = db.prepare('SELECT * FROM clubs WHERE id = ?').get(req.user.club_id)
  if (!club || !club.active) return fail(res, 403, 'Клуб отключён')
  req.club = club
  next()
}

function ownerOnly(req, res, next) {
  if (req.user.club_role !== 'owner') return fail(res, 403, 'Это может менять владелец клуба')
  next()
}

function checkPassword(user, password) {
  const hash = scryptSync(password, user.password_salt, 32)
  const stored = Buffer.from(user.password_hash, 'hex')
  return stored.length === hash.length && timingSafeEqual(stored, hash)
}

function text(value, max) {
  return String(value ?? '').trim().slice(0, max)
}

function slugify(value) {
  const map = { а: 'a', б: 'b', в: 'v', г: 'g', д: 'd', е: 'e', ё: 'e', ж: 'zh', з: 'z', и: 'i', й: 'i', к: 'k', л: 'l', м: 'm', н: 'n', о: 'o', п: 'p', р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'h', ц: 'c', ч: 'ch', ш: 'sh', щ: 'sch', ъ: '', ы: 'y', ь: '', э: 'e', ю: 'yu', я: 'ya' }
  const raw = text(value, 60).toLowerCase()
  const latin = [...raw].map((ch) => map[ch] ?? ch).join('')
  return latin.replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 40)
}

function intOrNull(value) {
  if (value === null || value === undefined || value === '') return null
  const n = Number(value)
  if (!Number.isInteger(n) || n < 0 || n > 100000) return null
  return n
}

const rarities = new Set(['common', 'uncommon', 'rare', 'epic', 'legendary'])
const prizeTypes = new Set(['bonus', 'time', 'bar', 'custom'])

function publicPrize(row, odds) {
  return {
    id: row.id,
    title: row.title,
    detail: row.detail,
    rarity: row.rarity,
    prizeType: row.prize_type,
    image: row.image,
    odds,
  }
}

function staffPrize(row) {
  return {
    id: row.id,
    title: row.title,
    detail: row.detail,
    rarity: row.rarity,
    prizeType: row.prize_type,
    weight: row.weight,
    cashierHint: row.cashier_hint,
    image: row.image,
    dailyLimit: row.daily_limit,
    totalLimit: row.total_limit,
    sortOrder: row.sort_order,
    active: !!row.active,
  }
}

function dayKey(date = new Date()) {
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Almaty',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date)
}

function prizesOf(caseId) {
  return db.prepare('SELECT * FROM prizes WHERE case_id = ? ORDER BY sort_order, title').all(caseId)
}

function oddsMap(prizes) {
  const active = prizes.filter((p) => p.active && p.weight > 0)
  const total = active.reduce((sum, p) => sum + p.weight, 0)
  const map = new Map()
  for (const prize of active) map.set(prize.id, total ? Math.round((prize.weight / total) * 1000) / 10 : 0)
  return map
}

function availablePrizes(caseId) {
  const prizes = prizesOf(caseId).filter((p) => p.active && p.weight > 0)
  const today = dayKey()
  return prizes.filter((prize) => {
    if (prize.total_limit != null) {
      const used = db.prepare(`SELECT COUNT(*) AS n FROM spins WHERE prize_id = ? AND status != 'void'`).get(prize.id).n
      if (used >= prize.total_limit) return false
    }
    if (prize.daily_limit != null) {
      if (prize.daily_limit === 0) return false
      const rows = db.prepare(`SELECT created_at FROM spins WHERE prize_id = ? AND status != 'void'`).all(prize.id)
      const todayCount = rows.filter((row) => dayKey(new Date(row.created_at)) === today).length
      if (todayCount >= prize.daily_limit) return false
    }
    return true
  })
}

function pickWeighted(prizes) {
  const total = prizes.reduce((sum, prize) => sum + prize.weight, 0)
  let roll = randomInt(total)
  for (const prize of prizes) {
    roll -= prize.weight
    if (roll < 0) return prize
  }
  return prizes[prizes.length - 1]
}

function makeCode(length) {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  let code = ''
  for (let i = 0; i < length; i++) code += alphabet[randomInt(alphabet.length)]
  return code
}

function uniqueCode(table, length) {
  for (let i = 0; i < 8; i++) {
    const code = makeCode(length)
    const row = db.prepare(`SELECT 1 FROM ${table} WHERE code = ?`).get(code)
    if (!row) return code
  }
  throw new Error('Не удалось выдать код')
}

function ipHash(req) {
  const ip = req.get('x-forwarded-for')?.split(',')[0].trim() || req.socket.remoteAddress || ''
  return createHash('sha256').update(`${secret}:${ip}`).digest('hex').slice(0, 24)
}

function publicCase(club, row) {
  const prizes = prizesOf(row.id).filter((p) => p.active && p.weight > 0)
  const odds = row.show_odds ? oddsMap(prizes) : null
  return {
    club: { slug: club.slug, name: club.name, city: club.city, tagline: club.tagline, accent: club.accent },
    case: {
      slug: row.slug,
      title: row.title,
      lead: row.lead,
      openMode: row.open_mode,
      showOdds: !!row.show_odds,
      prizes: prizes.map((prize) => publicPrize(prize, odds ? odds.get(prize.id) ?? 0 : undefined)),
    },
  }
}

function staffSpin(row) {
  return {
    id: row.id,
    code: row.code,
    title: row.prize_title,
    detail: row.prize_detail,
    rarity: row.prize_rarity,
    prizeType: row.prize_type,
    cashierHint: row.cashier_hint,
    image: row.prize_image,
    status: row.status,
    createdAt: row.created_at,
    creditedAt: row.credited_at,
    note: row.note,
  }
}

app.get('/api/health', (_req, res) => ok(res, { service: 'clutch' }))

app.post('/api/auth/login', wrap((req, res) => {
  const email = text(req.body?.email, 120).toLowerCase()
  const password = String(req.body?.password ?? '')
  const user = db.prepare('SELECT * FROM users WHERE email = ?').get(email)
  if (!user || !user.active || !checkPassword(user, password)) return fail(res, 401, 'Неверная почта или пароль')
  if (user.role === 'club') {
    const club = db.prepare('SELECT active FROM clubs WHERE id = ?').get(user.club_id)
    if (!club?.active) return fail(res, 403, 'Клуб отключён')
  }
  setSession(res, user.id)
  ok(res, profile(user))
}))

app.post('/api/auth/logout', (_req, res) => {
  clearSession(res)
  ok(res, { bye: true })
})

function profile(user) {
  const club = user.club_id ? db.prepare('SELECT slug, name FROM clubs WHERE id = ?').get(user.club_id) : null
  return {
    id: user.id,
    email: user.email,
    name: user.name,
    role: user.role,
    clubRole: user.club_role,
    club: club ? { slug: club.slug, name: club.name } : null,
  }
}

app.get('/api/me', auth, (req, res) => ok(res, profile(req.user)))

app.post('/api/me/password', auth, wrap((req, res) => {
  const next = String(req.body?.next ?? '')
  if (next.length < 8) return fail(res, 400, 'Новый пароль — от 8 символов')
  if (!checkPassword(req.user, String(req.body?.current ?? ''))) return fail(res, 400, 'Текущий пароль не подошёл')
  const hashed = hashPassword(next)
  db.prepare('UPDATE users SET password_hash = ?, password_salt = ? WHERE id = ?').run(hashed.hash, hashed.salt, req.user.id)
  ok(res, { changed: true })
}))

app.get('/api/admin/clubs', auth, adminOnly, (_req, res) => {
  const clubs = db.prepare('SELECT * FROM clubs ORDER BY created_at DESC').all().map((club) => {
    const owner = db.prepare(`SELECT name, email FROM users WHERE club_id = ? AND club_role = 'owner' ORDER BY created_at LIMIT 1`).get(club.id)
    const spins = db.prepare('SELECT COUNT(*) AS n FROM spins WHERE club_id = ?').get(club.id).n
    const waiting = db.prepare(`SELECT COUNT(*) AS n FROM spins WHERE club_id = ? AND status = 'open'`).get(club.id).n
    return {
      id: club.id,
      slug: club.slug,
      name: club.name,
      city: club.city,
      tagline: club.tagline,
      active: !!club.active,
      createdAt: club.created_at,
      owner,
      spins,
      waiting,
    }
  })
  ok(res, clubs)
})

app.post('/api/admin/clubs', auth, adminOnly, wrap((req, res) => {
  const name = text(req.body?.name, 80)
  const city = text(req.body?.city, 80)
  const ownerName = text(req.body?.ownerName, 80)
  const ownerEmail = text(req.body?.ownerEmail, 120).toLowerCase()
  const ownerPassword = String(req.body?.ownerPassword ?? '')
  const slug = slugify(req.body?.slug || name)
  if (name.length < 2) return fail(res, 400, 'Название клуба слишком короткое')
  if (!slug) return fail(res, 400, 'Адрес страницы пустой')
  if (!ownerEmail.includes('@') || ownerPassword.length < 8 || ownerName.length < 2) {
    return fail(res, 400, 'Владельцу нужны имя, почта и пароль от 8 символов')
  }
  if (db.prepare('SELECT 1 FROM clubs WHERE slug = ?').get(slug)) return fail(res, 400, 'Такой адрес страницы уже занят')
  if (db.prepare('SELECT 1 FROM users WHERE email = ?').get(ownerEmail)) return fail(res, 400, 'Почта владельца уже есть')

  const now = new Date().toISOString()
  const clubId = crypto.randomUUID()
  const caseId = crypto.randomUUID()
  const hashed = hashPassword(ownerPassword)
  db.exec('BEGIN')
  try {
    db.prepare(`INSERT INTO clubs (id, slug, name, city, tagline, accent, active, created_at) VALUES (?, ?, ?, ?, '', '#d6ff4a', 1, ?)`).run(clubId, slug, name, city, now)
    db.prepare(`INSERT INTO users (id, email, name, password_hash, password_salt, role, club_id, club_role, active, created_at) VALUES (?, ?, ?, ?, ?, 'club', ?, 'owner', 1, ?)`).run(crypto.randomUUID(), ownerEmail, ownerName, hashed.hash, hashed.salt, clubId, now)
    db.prepare(`INSERT INTO cases (id, club_id, slug, title, lead, open_mode, cooldown_seconds, show_odds, enabled, created_at) VALUES (?, ?, 'smena', 'Кейс смены', 'Назовите приз администратору.', 'ticket', 15, 0, 1, ?)`).run(caseId, clubId, now)
    db.prepare(`INSERT INTO prizes (id, case_id, title, detail, rarity, prize_type, weight, cashier_hint, image, sort_order, active) VALUES (?, ?, 'Бонус 200', 'На бонусный счёт', 'common', 'bonus', 1, 'В программе учёта: бонус +200. Комментарий CLUTCH.', '/art/prize-bonus.jpg', 1, 1)`).run(crypto.randomUUID(), caseId)
    db.exec('COMMIT')
  } catch (error) {
    db.exec('ROLLBACK')
    throw error
  }
  ok(res, { id: clubId, slug })
}))

app.patch('/api/admin/clubs/:id', auth, adminOnly, (req, res) => {
  const club = db.prepare('SELECT * FROM clubs WHERE id = ?').get(req.params.id)
  if (!club) return fail(res, 404, 'Клуб не найден')
  const active = req.body?.active == null ? club.active : req.body.active ? 1 : 0
  db.prepare('UPDATE clubs SET active = ? WHERE id = ?').run(active, club.id)
  ok(res, { active: !!active })
})

app.get('/api/club', auth, clubOnly, (req, res) => {
  const today = dayKey()
  const spins = db.prepare('SELECT created_at, status FROM spins WHERE club_id = ?').all(req.club.id)
  const todaySpins = spins.filter((row) => dayKey(new Date(row.created_at)) === today)
  ok(res, {
    id: req.club.id,
    slug: req.club.slug,
    name: req.club.name,
    city: req.club.city,
    tagline: req.club.tagline,
    accent: req.club.accent,
    role: req.user.club_role,
    today: todaySpins.length,
    waiting: spins.filter((row) => row.status === 'open').length,
    creditedToday: todaySpins.filter((row) => row.status === 'credited').length,
  })
})

app.patch('/api/club', auth, clubOnly, ownerOnly, (req, res) => {
  const name = text(req.body?.name, 80)
  const city = text(req.body?.city, 80)
  const tagline = text(req.body?.tagline, 160)
  const accent = text(req.body?.accent, 7)
  if (name.length < 2) return fail(res, 400, 'Название слишком короткое')
  if (!/^#[0-9a-fA-F]{6}$/.test(accent)) return fail(res, 400, 'Цвет в формате #RRGGBB')
  db.prepare('UPDATE clubs SET name = ?, city = ?, tagline = ?, accent = ? WHERE id = ?').run(name, city, tagline, accent, req.club.id)
  ok(res, { saved: true })
})

app.get('/api/club/cases', auth, clubOnly, (req, res) => {
  const cases = db.prepare('SELECT * FROM cases WHERE club_id = ? ORDER BY created_at').all(req.club.id).map((row) => ({
    id: row.id,
    slug: row.slug,
    title: row.title,
    lead: row.lead,
    openMode: row.open_mode,
    cooldownSeconds: row.cooldown_seconds,
    showOdds: !!row.show_odds,
    enabled: !!row.enabled,
    prizes: prizesOf(row.id).map(staffPrize),
  }))
  ok(res, cases)
})

app.post('/api/club/cases', auth, clubOnly, ownerOnly, (req, res) => {
  const title = text(req.body?.title, 80)
  const slug = slugify(req.body?.slug || title)
  if (title.length < 2 || !slug) return fail(res, 400, 'Нужны название и адрес кейса')
  if (db.prepare('SELECT 1 FROM cases WHERE club_id = ? AND slug = ?').get(req.club.id, slug)) return fail(res, 400, 'Такой адрес кейса уже есть')
  const caseId = crypto.randomUUID()
  db.prepare(`INSERT INTO cases (id, club_id, slug, title, lead, open_mode, cooldown_seconds, show_odds, enabled, created_at) VALUES (?, ?, ?, ?, '', 'ticket', 20, 0, 1, ?)`).run(caseId, req.club.id, slug, title, new Date().toISOString())
  ok(res, { id: caseId, slug })
})

app.patch('/api/club/cases/:id', auth, clubOnly, ownerOnly, (req, res) => {
  const row = db.prepare('SELECT * FROM cases WHERE id = ? AND club_id = ?').get(req.params.id, req.club.id)
  if (!row) return fail(res, 404, 'Кейс не найден')
  const title = text(req.body?.title ?? row.title, 80)
  const lead = text(req.body?.lead ?? row.lead, 240)
  const openMode = req.body?.openMode ?? row.open_mode
  const cooldown = Number(req.body?.cooldownSeconds ?? row.cooldown_seconds)
  const showOdds = req.body?.showOdds == null ? row.show_odds : req.body.showOdds ? 1 : 0
  const enabled = req.body?.enabled == null ? row.enabled : req.body.enabled ? 1 : 0
  if (title.length < 2) return fail(res, 400, 'Название слишком короткое')
  if (openMode !== 'public' && openMode !== 'ticket') return fail(res, 400, 'Режим открытия: страница или код')
  if (!Number.isInteger(cooldown) || cooldown < 0 || cooldown > 3600) return fail(res, 400, 'Пауза между открытиями: 0–3600 секунд')
  db.prepare(`UPDATE cases SET title = ?, lead = ?, open_mode = ?, cooldown_seconds = ?, show_odds = ?, enabled = ? WHERE id = ?`).run(title, lead, openMode, cooldown, showOdds, enabled, row.id)
  ok(res, { saved: true })
})

function readPrize(body, prev = {}) {
  const title = text(body.title ?? prev.title, 80)
  const detail = text(body.detail ?? prev.detail, 160)
  const rarity = body.rarity ?? prev.rarity
  const prizeType = body.prizeType ?? prev.prize_type
  const weight = Number(body.weight ?? prev.weight)
  const cashierHint = text(body.cashierHint ?? prev.cashier_hint, 300)
  const image = body.image === undefined ? prev.image : text(body.image, 200) || null
  const dailyLimit = body.dailyLimit === undefined ? prev.daily_limit : intOrNull(body.dailyLimit)
  const totalLimit = body.totalLimit === undefined ? prev.total_limit : intOrNull(body.totalLimit)
  const active = body.active == null ? (prev.active ?? 1) : body.active ? 1 : 0
  if (title.length < 2) return 'Название приза слишком короткое'
  if (!rarities.has(rarity)) return 'Редкость не из списка'
  if (!prizeTypes.has(prizeType)) return 'Тип приза не из списка'
  if (!Number.isInteger(weight) || weight < 1 || weight > 100000) return 'Вес приза: целое число от 1'
  if (cashierHint.length < 2) return 'Напишите, что сотруднику ввести в программе учёта'
  if (body.dailyLimit !== undefined && body.dailyLimit !== '' && body.dailyLimit != null && dailyLimit == null) return 'Дневной лимит — целое число'
  if (body.totalLimit !== undefined && body.totalLimit !== '' && body.totalLimit != null && totalLimit == null) return 'Общий лимит — целое число'
  return { title, detail, rarity, prizeType, weight, cashierHint, image, dailyLimit, totalLimit, active }
}

app.post('/api/club/cases/:id/prizes', auth, clubOnly, ownerOnly, (req, res) => {
  const row = db.prepare('SELECT id FROM cases WHERE id = ? AND club_id = ?').get(req.params.id, req.club.id)
  if (!row) return fail(res, 404, 'Кейс не найден')
  const prize = readPrize(req.body)
  if (typeof prize === 'string') return fail(res, 400, prize)
  const sort = (db.prepare('SELECT COALESCE(MAX(sort_order), 0) AS n FROM prizes WHERE case_id = ?').get(row.id).n || 0) + 1
  const prizeId = crypto.randomUUID()
  db.prepare(`INSERT INTO prizes (id, case_id, title, detail, rarity, prize_type, weight, cashier_hint, image, daily_limit, total_limit, sort_order, active) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`).run(prizeId, row.id, prize.title, prize.detail, prize.rarity, prize.prizeType, prize.weight, prize.cashierHint, prize.image, prize.dailyLimit, prize.totalLimit, sort, prize.active)
  ok(res, { id: prizeId })
})

app.patch('/api/club/prizes/:id', auth, clubOnly, ownerOnly, (req, res) => {
  const prev = db.prepare(`
    SELECT prizes.* FROM prizes JOIN cases ON cases.id = prizes.case_id
    WHERE prizes.id = ? AND cases.club_id = ?
  `).get(req.params.id, req.club.id)
  if (!prev) return fail(res, 404, 'Приз не найден')
  const prize = readPrize(req.body, prev)
  if (typeof prize === 'string') return fail(res, 400, prize)
  db.prepare(`UPDATE prizes SET title = ?, detail = ?, rarity = ?, prize_type = ?, weight = ?, cashier_hint = ?, image = ?, daily_limit = ?, total_limit = ?, active = ? WHERE id = ?`).run(prize.title, prize.detail, prize.rarity, prize.prizeType, prize.weight, prize.cashierHint, prize.image, prize.dailyLimit, prize.totalLimit, prize.active, prev.id)
  ok(res, { saved: true })
})

app.delete('/api/club/prizes/:id', auth, clubOnly, ownerOnly, (req, res) => {
  const prev = db.prepare(`
    SELECT prizes.id FROM prizes JOIN cases ON cases.id = prizes.case_id
    WHERE prizes.id = ? AND cases.club_id = ?
  `).get(req.params.id, req.club.id)
  if (!prev) return fail(res, 404, 'Приз не найден')
  db.prepare('DELETE FROM prizes WHERE id = ?').run(prev.id)
  ok(res, { deleted: true })
})

app.post('/api/club/upload', auth, clubOnly, ownerOnly, (req, res) => {
  const match = /^data:image\/(png|jpeg|webp);base64,([A-Za-z0-9+/=\s]+)$/.exec(String(req.body?.dataUrl ?? ''))
  if (!match) return fail(res, 400, 'Нужна картинка PNG, JPEG или WebP')
  const buf = Buffer.from(match[2], 'base64')
  if (buf.length < 32 || buf.length > 1_500_000) return fail(res, 400, 'Картинка от 32 байт до 1.5 МБ')
  const ext = match[1] === 'jpeg' ? 'jpg' : match[1]
  const name = `${crypto.randomUUID()}.${ext}`
  writeFileSync(join(uploadDir, name), buf)
  ok(res, { url: `/uploads/${name}` })
})

app.get('/api/club/staff', auth, clubOnly, ownerOnly, (req, res) => {
  const staff = db.prepare(`SELECT id, name, email, club_role, active, created_at FROM users WHERE club_id = ? ORDER BY created_at`).all(req.club.id).map((row) => ({
    id: row.id,
    name: row.name,
    email: row.email,
    clubRole: row.club_role,
    active: !!row.active,
    createdAt: row.created_at,
  }))
  ok(res, staff)
})

app.post('/api/club/staff', auth, clubOnly, ownerOnly, (req, res) => {
  const name = text(req.body?.name, 80)
  const email = text(req.body?.email, 120).toLowerCase()
  const password = String(req.body?.password ?? '')
  const clubRole = req.body?.clubRole === 'owner' ? 'owner' : 'staff'
  if (name.length < 2 || !email.includes('@') || password.length < 8) return fail(res, 400, 'Имя, почта и пароль от 8 символов')
  if (db.prepare('SELECT 1 FROM users WHERE email = ?').get(email)) return fail(res, 400, 'Такая почта уже есть')
  const hashed = hashPassword(password)
  db.prepare(`INSERT INTO users (id, email, name, password_hash, password_salt, role, club_id, club_role, active, created_at) VALUES (?, ?, ?, ?, ?, 'club', ?, ?, 1, ?)`).run(crypto.randomUUID(), email, name, hashed.hash, hashed.salt, req.club.id, clubRole, new Date().toISOString())
  ok(res, { created: true })
})

app.patch('/api/club/staff/:id', auth, clubOnly, ownerOnly, (req, res) => {
  const person = db.prepare('SELECT * FROM users WHERE id = ? AND club_id = ?').get(req.params.id, req.club.id)
  if (!person) return fail(res, 404, 'Сотрудник не найден')
  if (person.id === req.user.id && req.body?.active === false) return fail(res, 400, 'Себя отключать нельзя')
  const active = req.body?.active == null ? person.active : req.body.active ? 1 : 0
  db.prepare('UPDATE users SET active = ? WHERE id = ?').run(active, person.id)
  ok(res, { active: !!active })
})

app.get('/api/club/spins', auth, clubOnly, (req, res) => {
  const status = req.query.status === 'open' || req.query.status === 'credited' || req.query.status === 'void' ? req.query.status : null
  const rows = status
    ? db.prepare('SELECT * FROM spins WHERE club_id = ? AND status = ? ORDER BY created_at DESC LIMIT 100').all(req.club.id, status)
    : db.prepare('SELECT * FROM spins WHERE club_id = ? ORDER BY created_at DESC LIMIT 100').all(req.club.id)
  ok(res, rows.map(staffSpin))
})

function spinOfClub(req) {
  return db.prepare('SELECT * FROM spins WHERE id = ? AND club_id = ?').get(req.params.id, req.club.id)
}

app.post('/api/club/spins/:id/credit', auth, clubOnly, (req, res) => {
  const spin = spinOfClub(req)
  if (!spin) return fail(res, 404, 'Открытие не найдено')
  if (spin.status !== 'open') return fail(res, 400, 'Это открытие уже обработано')
  const note = text(req.body?.note, 200)
  db.prepare(`UPDATE spins SET status = 'credited', credited_at = ?, credited_by = ?, note = ? WHERE id = ?`).run(new Date().toISOString(), req.user.id, note || null, spin.id)
  ok(res, { status: 'credited' })
})

app.post('/api/club/spins/:id/void', auth, clubOnly, (req, res) => {
  const spin = spinOfClub(req)
  if (!spin) return fail(res, 404, 'Открытие не найдено')
  if (spin.status !== 'open') return fail(res, 400, 'Это открытие уже обработано')
  db.prepare(`UPDATE spins SET status = 'void', credited_at = ?, credited_by = ?, note = ? WHERE id = ?`).run(new Date().toISOString(), req.user.id, text(req.body?.note, 200) || null, spin.id)
  ok(res, { status: 'void' })
})

app.post('/api/club/cases/:id/tickets', auth, clubOnly, (req, res) => {
  const row = db.prepare('SELECT * FROM cases WHERE id = ? AND club_id = ?').get(req.params.id, req.club.id)
  if (!row) return fail(res, 404, 'Кейс не найден')
  const count = Math.min(20, Math.max(1, Number(req.body?.count) || 1))
  const created = []
  for (let i = 0; i < count; i++) {
    const code = uniqueCode('tickets', 6)
    db.prepare('INSERT INTO tickets (id, case_id, code, created_by, created_at) VALUES (?, ?, ?, ?, ?)').run(crypto.randomUUID(), row.id, code, req.user.id, new Date().toISOString())
    created.push(code)
  }
  ok(res, { codes: created })
})

app.get('/api/club/cases/:id/tickets', auth, clubOnly, (req, res) => {
  const row = db.prepare('SELECT id FROM cases WHERE id = ? AND club_id = ?').get(req.params.id, req.club.id)
  if (!row) return fail(res, 404, 'Кейс не найден')
  const tickets = db.prepare('SELECT code, created_at, used_at FROM tickets WHERE case_id = ? ORDER BY created_at DESC LIMIT 30').all(row.id)
  ok(res, tickets.map((ticket) => ({ code: ticket.code, createdAt: ticket.created_at, usedAt: ticket.used_at })))
})

app.get('/api/public/:slug', (req, res) => {
  const club = db.prepare('SELECT * FROM clubs WHERE slug = ? AND active = 1').get(req.params.slug)
  if (!club) return fail(res, 404, 'Клуб не найден')
  const cases = db.prepare('SELECT slug, title, lead FROM cases WHERE club_id = ? AND enabled = 1 ORDER BY created_at').all(club.id)
  ok(res, { slug: club.slug, name: club.name, city: club.city, tagline: club.tagline, accent: club.accent, cases })
})

app.get('/api/public/:slug/:caseSlug', (req, res) => {
  const found = loadPublicCase(req.params.slug, req.params.caseSlug)
  if (found.error) return fail(res, found.status, found.error)
  ok(res, publicCase(found.club, found.caseRow))
})

function loadPublicCase(slug, caseSlug) {
  const club = db.prepare('SELECT * FROM clubs WHERE slug = ? AND active = 1').get(slug)
  if (!club) return { status: 404, error: 'Клуб не найден' }
  const caseRow = db.prepare('SELECT * FROM cases WHERE club_id = ? AND slug = ? AND enabled = 1').get(club.id, caseSlug)
  if (!caseRow) return { status: 404, error: 'Кейс выключен' }
  return { club, caseRow }
}

app.post('/api/public/:slug/:caseSlug/spin', wrap((req, res) => {
  const found = loadPublicCase(req.params.slug, req.params.caseSlug)
  if (found.error) return fail(res, found.status, found.error)
  const { club, caseRow } = found
  const hash = ipHash(req)

  if (caseRow.open_mode === 'ticket') {
    const ticketCode = text(req.body?.ticket, 12).toUpperCase()
    const ticket = db.prepare('SELECT * FROM tickets WHERE case_id = ? AND code = ?').get(caseRow.id, ticketCode)
    if (!ticket) return fail(res, 400, 'Код открытия не найден')
    if (ticket.used_at) return fail(res, 400, 'Этот код уже использован')
  } else if (caseRow.cooldown_seconds > 0) {
    const last = db.prepare('SELECT created_at FROM spins WHERE case_id = ? AND ip_hash = ? ORDER BY created_at DESC LIMIT 1').get(caseRow.id, hash)
    if (last) {
      const wait = caseRow.cooldown_seconds * 1000 - (Date.now() - new Date(last.created_at).getTime())
      if (wait > 0) return fail(res, 429, `Следующее открытие через ${Math.ceil(wait / 1000)} сек.`)
    }
  }

  const pool = availablePrizes(caseRow.id)
  if (pool.length === 0) return fail(res, 400, 'Призы этого кейса на сегодня закончились')
  const winner = pickWeighted(pool)
  const length = 36
  const landing = 29
  const strip = Array.from({ length }, () => pool[randomInt(pool.length)])
  strip[landing] = winner
  const code = uniqueCode('spins', 4)
  const spinId = crypto.randomUUID()
  const now = new Date().toISOString()

  db.exec('BEGIN')
  try {
    if (caseRow.open_mode === 'ticket') {
      const ticketCode = text(req.body?.ticket, 12).toUpperCase()
      const updated = db.prepare('UPDATE tickets SET used_at = ?, spin_id = ? WHERE case_id = ? AND code = ? AND used_at IS NULL').run(now, spinId, caseRow.id, ticketCode)
      if (Number(updated.changes) !== 1) {
        db.exec('ROLLBACK')
        return fail(res, 400, 'Этот код уже использован')
      }
    }
    db.prepare(`
      INSERT INTO spins (id, club_id, case_id, prize_id, code, prize_title, prize_detail, prize_rarity, prize_type, cashier_hint, prize_image, status, ip_hash, created_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'open', ?, ?)
    `).run(spinId, club.id, caseRow.id, winner.id, code, winner.title, winner.detail, winner.rarity, winner.prize_type, winner.cashier_hint, winner.image, hash, now)
    db.exec('COMMIT')
  } catch (error) {
    db.exec('ROLLBACK')
    throw error
  }

  const odds = caseRow.show_odds ? oddsMap(pool) : null
  ok(res, {
    code,
    landing,
    prize: publicPrize(winner, odds ? odds.get(winner.id) : undefined),
    strip: strip.map((prize) => publicPrize(prize)),
  })
}))

const dist = join(root, 'dist')
if (process.env.NODE_ENV === 'production' && existsSync(dist)) {
  app.use(express.static(dist))
  app.get(/^(?!\/api|\/uploads).*/, (_req, res) => {
    res.sendFile(join(dist, 'index.html'))
  })
}

app.use((error, _req, res, _next) => {
  console.error(error)
  if (!res.headersSent) fail(res, 500, 'Сервис временно не ответил')
})

app.listen(port, '127.0.0.1', () => {
  console.log(`CLUTCH API http://127.0.0.1:${port}`)
})
