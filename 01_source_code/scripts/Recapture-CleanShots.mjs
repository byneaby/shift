import { chromium } from 'playwright';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.join(__dirname, '..', 'src', 'ShiftClub.Server', 'wwwroot', 'product', 'shots');

const browser = await chromium.launch({ headless: true });
const page = await (
  await browser.newContext({ viewport: { width: 1440, height: 900 } })
).newPage();

await page.goto('http://127.0.0.1:5080/login', { waitUntil: 'networkidle' });
await page.locator('input[type="text"]').first().fill('owner');
await page.locator('input[type="password"]').first().fill('Owner123!');
await page.locator('button[type="submit"], button:has-text("Войти")').first().click();
await page.waitForTimeout(2000);

for (const [route, name] of [
  ['/cash', '03-cash'],
  ['/bar', '04-bar'],
  ['/bookings', '05-bookings'],
  ['/customers', '06-customers'],
  ['/reports', '07-reports'],
]) {
  await page.goto(`http://127.0.0.1:5080${route}`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(900);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(250);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(500);
  // click backdrop / cancel if still open
  const cancel = page.locator('button:has-text("Отмена"), button:has-text("Закрыть")').first();
  if (await cancel.isVisible().catch(() => false)) await cancel.click().catch(() => {});
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUT, `${name}.jpg`), type: 'jpeg', quality: 82 });
  console.log('ok', name);
}

await browser.close();
