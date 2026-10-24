import { DatabaseSync } from 'node:sqlite'
import { mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { randomBytes, scryptSync } from 'node:crypto'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const dataDir = join(root, 'data')
mkdirSync(dataDir, { recursive: true })

const db = new DatabaseSync(join(dataDir, 'clutch.db'))
db.exec('PRAGMA journal_mode = WAL')
db.exec('PRAGMA foreign_keys = ON')

db.exec(`
CREATE TABLE IF NOT EXISTS users (
  id TEXT PRIMARY KEY,
  email TEXT UNIQUE NOT NULL,
  name TEXT NOT NULL,
  password_hash TEXT NOT NULL,
  password_salt TEXT NOT NULL,
  role TEXT NOT NULL,
  club_id TEXT,
  club_role TEXT,
  active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS clubs (
  id TEXT PRIMARY KEY,
  slug TEXT UNIQUE NOT NULL,
  name TEXT NOT NULL,
  city TEXT NOT NULL DEFAULT '',
  tagline TEXT NOT NULL DEFAULT '',
  accent TEXT NOT NULL DEFAULT '#d6ff4a',
  active INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS cases (
  id TEXT PRIMARY KEY,
  club_id TEXT NOT NULL,
  slug TEXT NOT NULL,
  title TEXT NOT NULL,
  lead TEXT NOT NULL DEFAULT '',
  open_mode TEXT NOT NULL DEFAULT 'public',
  cooldown_seconds INTEGER NOT NULL DEFAULT 20,
  show_odds INTEGER NOT NULL DEFAULT 0,
  enabled INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  UNIQUE (club_id, slug)
);

CREATE TABLE IF NOT EXISTS prizes (
  id TEXT PRIMARY KEY,
  case_id TEXT NOT NULL,
  title TEXT NOT NULL,
  detail TEXT NOT NULL DEFAULT '',
  rarity TEXT NOT NULL,
  prize_type TEXT NOT NULL,
  weight INTEGER NOT NULL,
  cashier_hint TEXT NOT NULL DEFAULT '',
  image TEXT,
  daily_limit INTEGER,
  total_limit INTEGER,
  sort_order INTEGER NOT NULL DEFAULT 0,
  active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS spins (
  id TEXT PRIMARY KEY,
  club_id TEXT NOT NULL,
  case_id TEXT NOT NULL,
  prize_id TEXT NOT NULL,
  code TEXT UNIQUE NOT NULL,
  prize_title TEXT NOT NULL,
  prize_detail TEXT NOT NULL DEFAULT '',
  prize_rarity TEXT NOT NULL,
  prize_type TEXT NOT NULL,
  cashier_hint TEXT NOT NULL DEFAULT '',
  prize_image TEXT,
  status TEXT NOT NULL DEFAULT 'open',
  ip_hash TEXT,
  created_at TEXT NOT NULL,
  credited_at TEXT,
  credited_by TEXT,
  note TEXT
);

CREATE TABLE IF NOT EXISTS tickets (
  id TEXT PRIMARY KEY,
  case_id TEXT NOT NULL,
  code TEXT UNIQUE NOT NULL,
  created_by TEXT,
  created_at TEXT NOT NULL,
  used_at TEXT,
  spin_id TEXT
);

CREATE INDEX IF NOT EXISTS idx_spins_case_time ON spins (case_id, created_at);
CREATE INDEX IF NOT EXISTS idx_spins_club_time ON spins (club_id, created_at);
`)

export function hashPassword(password, salt = randomBytes(16).toString('hex')) {
  const hash = scryptSync(password, salt, 32).toString('hex')
  return { hash, salt }
}

function id() {
  return crypto.randomUUID()
}

function seed() {
  const count = db.prepare('SELECT COUNT(*) AS n FROM users').get().n
  if (count > 0) return

  const now = new Date().toISOString()
  const admin = hashPassword('ClutchAdmin2026')
  const owner = hashPassword('NexusOwner2026')
  const desk = hashPassword('NexusDesk2026')
  const clubId = id()
  const caseId = id()

  const insertUser = db.prepare(`
    INSERT INTO users (id, email, name, password_hash, password_salt, role, club_id, club_role, active, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?)
  `)

  db.exec('BEGIN')
  try {
    insertUser.run(id(), 'admin@clutch.local', 'Админ CLUTCH', admin.hash, admin.salt, 'admin', null, null, now)
    db.prepare(`
      INSERT INTO clubs (id, slug, name, city, tagline, accent, active, created_at)
      VALUES (?, 'nexus', 'Nexus Arena', 'Астана', 'Кейс смены на стойке', '#d6ff4a', 1, ?)
    `).run(clubId, now)
    insertUser.run(id(), 'owner@nexus.local', 'Владелец Nexus', owner.hash, owner.salt, 'club', clubId, 'owner', now)
    insertUser.run(id(), 'desk@nexus.local', 'Смена Nexus', desk.hash, desk.salt, 'club', clubId, 'staff', now)
    db.prepare(`
      INSERT INTO cases (id, club_id, slug, title, lead, open_mode, cooldown_seconds, show_odds, enabled, created_at)
      VALUES (?, ?, 'smena', 'Кейс смены', 'Крутите после пополнения. Приз называете администратору.', 'public', 15, 0, 1, ?)
    `).run(caseId, clubId, now)

    const prize = db.prepare(`
      INSERT INTO prizes (id, case_id, title, detail, rarity, prize_type, weight, cashier_hint, image, daily_limit, total_limit, sort_order, active)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1)
    `)
    prize.run(id(), caseId, '30 минут', 'Общий зал', 'common', 'time', 40, 'В программе учёта: время, общий зал, 30 минут. Комментарий CLUTCH.', '/art/prize-time.jpg', null, null, 1)
    prize.run(id(), caseId, 'Бонус 200', 'На бонусный счёт', 'common', 'bonus', 30, 'В программе учёта: бонус +200. Комментарий CLUTCH.', '/art/prize-bonus.jpg', null, null, 2)
    prize.run(id(), caseId, 'Напиток', 'Любой 0.25 с бара', 'uncommon', 'bar', 16, 'Выдать напиток 0.25 с бара и списать его в программе учёта.', '/art/prize-drink.jpg', 20, null, 3)
    prize.run(id(), caseId, 'Бонус 500', 'На бонусный счёт', 'rare', 'bonus', 9, 'В программе учёта: бонус +500. Комментарий CLUTCH.', '/art/prize-bonus.jpg', 10, null, 4)
    prize.run(id(), caseId, 'Час ночи', 'Ночной пакет', 'epic', 'time', 4, 'В программе учёта: ночной час. Комментарий CLUTCH.', '/art/rune-stone.jpg', 4, null, 5)
    prize.run(id(), caseId, 'Гарнитура', 'Забрать у администратора', 'legendary', 'custom', 1, 'Выдать гарнитуру со склада. В программе учёта отметить выдачу товара.', '/art/prize-headset.jpg', 1, 3, 6)
    db.exec('COMMIT')
  } catch (error) {
    db.exec('ROLLBACK')
    throw error
  }
}

seed()

export { db, dataDir, root }
