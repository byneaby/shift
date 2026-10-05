import { chromium } from 'playwright';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.SHIFT_BASE || 'http://127.0.0.1:5080';
const LOGIN = process.env.SHIFT_LOGIN || 'owner';
const PASS = process.env.SHIFT_PASS || 'Owner123!';
const OUT = path.join(__dirname, '..', 'src', 'ShiftClub.Server', 'wwwroot', 'product', 'shots');

async function shot(page, name) {
  const file = path.join(OUT, `${name}.jpg`);
  await page.waitForTimeout(900);
  await page.screenshot({ path: file, type: 'jpeg', quality: 82, fullPage: false });
  console.log('saved', name, fs.statSync(file).size);
}

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  deviceScaleFactor: 1,
});
const page = await context.newPage();

await page.goto(`${BASE}/login`, { waitUntil: 'networkidle', timeout: 60000 });
await page.waitForTimeout(600);

const user = page.locator('input[type="text"], input[name="login"], input[autocomplete="username"]').first();
const pass = page.locator('input[type="password"]').first();
await user.fill(LOGIN);
await pass.fill(PASS);
await page.locator('button[type="submit"], button:has-text("Войти")').first().click();
await page.waitForURL((url) => !url.pathname.includes('/login'), { timeout: 30000 }).catch(() => {});
await page.waitForTimeout(1800);
await shot(page, '01-home');

const routes = [
  ['/floor', '02-floor'],
  ['/cash', '03-cash'],
  ['/bar', '04-bar'],
  ['/bookings', '05-bookings'],
  ['/customers', '06-customers'],
  ['/reports', '07-reports'],
];

for (const [route, name] of routes) {
  await page.goto(`${BASE}${route}`, { waitUntil: 'networkidle', timeout: 60000 });
  await page.waitForTimeout(1400);
  const close = page.locator('button:has-text("Закрыть"), button:has-text("Отмена")').first();
  if (await close.isVisible().catch(() => false)) {
    await close.click().catch(() => {});
    await page.waitForTimeout(300);
  }
  await shot(page, name);
}

await browser.close();
console.log('done');
