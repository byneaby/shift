import { chromium } from 'playwright';
import path from 'path';
import { fileURLToPath } from 'url';
import fs from 'fs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.join(__dirname, '..', 'src', 'ShiftClub.Server', 'wwwroot', 'product', 'shots');
const auth = JSON.parse(fs.readFileSync('C:/Temp/auth.json', 'utf8'));
const token = auth.data.accessToken;
const employee = auth.data.employee;

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
await context.addInitScript(
  ({ token, employee }) => {
    localStorage.setItem('shiftclub.token', token);
    localStorage.setItem('shiftclub.employee', JSON.stringify(employee));
  },
  { token, employee },
);

const page = await context.newPage();
for (const [route, name] of [
  ['/cash', '03-cash'],
  ['/bar', '04-bar'],
  ['/bookings', '05-bookings'],
]) {
  await page.goto(`http://127.0.0.1:5080${route}`, { waitUntil: 'networkidle', timeout: 60000 });
  await page.waitForTimeout(1200);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  const btn = page.locator('button:has-text("Отмена")').first();
  if (await btn.isVisible().catch(() => false)) await btn.click().catch(() => {});
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(OUT, `${name}.jpg`), type: 'jpeg', quality: 82 });
  console.log('ok', name);
}
await browser.close();
