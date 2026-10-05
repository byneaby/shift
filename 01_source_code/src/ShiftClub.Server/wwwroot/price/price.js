/* SHIFT Cyber Club — экран прайса.
   Цены берутся из API /api/public/price (панель → БД).
   FALLBACK — только если API недоступен. */

const FALLBACK = {
  currency: "T",
  dayFrom: 12,
  dayTo: 18,
  nightFrom: 23,
  nightTo: 8,
  zones: [
    {
      id: "standard",
      name: "STANDARD",
      color: "#ff6a00",
      rows: [
        { key: "hour", icon: "i-clock", label: "Час", note: "", price: 500 },
        { key: "combo21", icon: "i-gift", label: "2 + 1", note: "2 часа · третий в подарок", price: 1000 },
        { key: "combo32", icon: "i-gift", label: "3 + 2", note: "3 часа · +2 в подарок", price: 1500 },
        { key: "day", icon: "i-sun", label: "День", note: "12:00 — 18:00", price: 1500 },
        { key: "night", icon: "i-moon", label: "Ночь", note: "23:00 — 08:00", price: 2000 },
      ],
    },
    {
      id: "bootcamp",
      name: "BOOTCAMP",
      color: "#17a3ff",
      rows: [
        { key: "hour", icon: "i-clock", label: "Час", note: "", price: 750 },
        { key: "combo21", icon: "i-gift", label: "2 + 1", note: "2 часа · третий в подарок", price: 1500 },
        { key: "combo32", icon: "i-gift", label: "3 + 2", note: "3 часа · +2 в подарок", price: 2000 },
        { key: "day", icon: "i-sun", label: "День", note: "12:00 — 18:00", price: 2000 },
        { key: "night", icon: "i-moon", label: "Ночь", note: "23:00 — 08:00", price: 2500 },
      ],
    },
    {
      id: "vip",
      name: "VIP",
      color: "#ffc021",
      rows: [
        { key: "hour", icon: "i-clock", label: "Час", note: "", price: 750 },
        { key: "combo21", icon: "i-gift", label: "2 + 1", note: "2 часа · третий в подарок", price: 1500 },
        { key: "combo32", icon: "i-gift", label: "3 + 2", note: "3 часа · +2 в подарок", price: 2000 },
        { key: "day", icon: "i-sun", label: "День", note: "12:00 — 18:00", price: 2000 },
        { key: "night", icon: "i-moon", label: "Ночь", note: "23:00 — 08:00", price: 2500 },
      ],
    },
  ],
  extras: {
    title: "КАЛЬЯН",
    items: [
      { id: "light", label: "Лайт", note: "мягкий микс", price: 3500 },
      { id: "hard", label: "Хард", note: "крепкий микс", price: 6500 },
      { id: "bowl", label: "Замена чаши", note: "", price: 2000 },
    ],
  },
};

const state = {
  currency: FALLBACK.currency,
  dayFrom: FALLBACK.dayFrom,
  dayTo: FALLBACK.dayTo,
  nightFrom: FALLBACK.nightFrom,
  nightTo: FALLBACK.nightTo,
  zones: FALLBACK.zones,
  extras: FALLBACK.extras,
  promo: null,
  depositBonus: null,
  source: "fallback",
};

const nf = new Intl.NumberFormat("ru-RU");
const $ = (id) => document.getElementById(id);
const club = { botUsername: "", address: "", city: "" };

let spotlightTimer = 0;
let firstPaint = true;
let themeHudTimer = 0;

const THEMES = [
  { id: "arena", name: "ARENA", hint: "Кибер · 3 колонки" },
  { id: "ledger", name: "LEDGER", hint: "Ведомость · таблица" },
  { id: "boulevard", name: "BOULEVARD", hint: "Горизонтальные полосы" },
  { id: "neon", name: "NEON", hint: "Неон · асимметрия" },
  { id: "cinema", name: "CINEMA", hint: "Кино · крупные цены" },
];

const THEME_STORAGE_KEY = "shiftclub.priceTheme";
let themeIndex = 0;

function readStoredThemeIndex() {
  try {
    const raw = localStorage.getItem(THEME_STORAGE_KEY);
    const n = Number(raw);
    if (Number.isInteger(n) && n >= 0 && n < THEMES.length) return n;
  } catch {
    /* ignore */
  }
  return 0;
}

function applyTheme(index, { announce = false } = {}) {
  themeIndex = ((index % THEMES.length) + THEMES.length) % THEMES.length;
  const theme = THEMES[themeIndex];
  THEMES.forEach((t) => document.body.classList.remove(`theme-${t.id}`));
  document.body.classList.add(`theme-${theme.id}`);
  try {
    localStorage.setItem(THEME_STORAGE_KEY, String(themeIndex));
  } catch {
    /* ignore */
  }

  const nameEl = $("themeName");
  const idxEl = $("themeIndex");
  const hud = $("themeHud");
  if (nameEl) nameEl.textContent = theme.name;
  if (idxEl) idxEl.textContent = `${themeIndex + 1} / ${THEMES.length}`;
  if (hud) {
    hud.title = theme.hint;
    if (announce) {
      document.body.classList.remove("is-idle");
      hud.style.opacity = "1";
      window.clearTimeout(themeHudTimer);
      themeHudTimer = window.setTimeout(() => {
        if (document.body.classList.contains("is-idle")) hud.style.opacity = "";
      }, 2200);
    }
  }

  // Перерисовать карточки — у тем разная плотность/акценты.
  firstPaint = false;
  renderBoard();
}

function cycleTheme(delta = 1) {
  applyTheme(themeIndex + delta, { announce: true });
}

function applyBoard(data) {
  if (!data || !Array.isArray(data.zones) || data.zones.length === 0) return false;
  state.currency = data.currency || "T";
  const w = data.windows || {};
  state.dayFrom = Number(w.dayFromHour ?? state.dayFrom);
  state.dayTo = Number(w.dayToHour ?? state.dayTo);
  state.nightFrom = Number(w.nightFromHour ?? state.nightFrom);
  state.nightTo = Number(w.nightToHour ?? state.nightTo);
  state.zones = data.zones.map((z) => ({
    id: z.id || z.code || z.name,
    name: z.name,
    color: z.color || "#ff6a00",
    rows: (z.rows || []).map((r) => ({
      key: r.key,
      icon: r.icon || "i-info",
      label: r.label,
      note: r.note || "",
      price: Number(r.price) || 0,
      originalPrice:
        r.originalPrice != null && Number(r.originalPrice) > Number(r.price)
          ? Number(r.originalPrice)
          : null,
    })),
  }));
  state.extras =
    data.extras && Array.isArray(data.extras.items) && data.extras.items.length
      ? {
          title: data.extras.title || "КАЛЬЯН",
          items: data.extras.items.map((it) => ({
            id: it.id,
            label: it.label,
            note: it.note || "",
            price: Number(it.price) || 0,
          })),
        }
      : null;
  state.promo =
    data.promo && data.promo.active
      ? {
          percent: Number(data.promo.percent) || 0,
          label: data.promo.label || `−${Number(data.promo.percent) || 0}%`,
          title: data.promo.title || "Акция",
          endsAt: data.promo.endsAt || null,
        }
      : null;
  state.depositBonus =
    data.depositBonus && data.depositBonus.enabled && Array.isArray(data.depositBonus.tiers)
      ? {
          tiers: data.depositBonus.tiers
            .map((t) => ({
              minAmount: Number(t.minAmount) || 0,
              bonusAmount: Number(t.bonusAmount) || 0,
            }))
            .filter((t) => t.minAmount > 0 && t.bonusAmount > 0),
        }
      : null;
  if (state.depositBonus && !state.depositBonus.tiers.length) state.depositBonus = null;
  state.source = "api";
  return true;
}

function buildCards() {
  const host = $("cards");
  host.innerHTML = "";
  const cols = Math.min(4, Math.max(1, state.zones.length));
  host.style.setProperty("--cols", String(cols));

  state.zones.forEach((zone, i) => {
    const card = document.createElement("article");
    card.className = "card";
    card.dataset.zone = zone.id;
    card.style.setProperty("--c", zone.color);
    card.style.setProperty("--i", String(i));

    const compact = zone.rows.length <= 2;
    const rows = zone.rows
      .map(
        (row, r) => `
        <div class="row" data-key="${row.key}" style="--r:${r}">
          <svg class="row__ico"><use href="#${row.icon}" /></svg>
          <span class="row__label">
            <b>${row.label}</b>
            ${row.note ? `<em>${row.note}</em>` : ""}
          </span>
          <span class="row__price-wrap">
            ${
              row.originalPrice
                ? `<s class="row__was">${nf.format(row.originalPrice)}<span>${state.currency}</span></s>`
                : ""
            }
            <span class="row__price" data-value="${row.price}">0<span>${state.currency}</span></span>
          </span>
        </div>`,
      )
      .join("");

    card.innerHTML = `
      <div class="card__inner${compact ? " card__inner--compact" : ""}">
        <header class="card__head">
          <h2 class="card__name">${zone.name}</h2>
          <div class="card__head-meta">
            ${
              state.promo
                ? `<div class="card__sale">−${Math.round(state.promo.percent)}%</div>`
                : ""
            }
            <span class="card__slashes"><i style="--s:0"></i><i style="--s:1"></i><i style="--s:2"></i></span>
          </div>
        </header>
        <div class="card__rows" style="--rowcount:${zone.rows.length}">${rows}</div>
      </div>`;
    host.appendChild(card);
  });
}

function buildPromoBadge() {
  const badge = $("promoBadge");
  if (!badge) return;
  if (!state.promo) {
    badge.hidden = true;
    document.body.classList.remove("has-promo");
    return;
  }
  document.body.classList.add("has-promo");
  badge.hidden = false;
  const title = $("promoTitle");
  const label = $("promoLabel");
  const until = $("promoUntil");
  if (title) title.textContent = state.promo.title || "Акция";
  if (label) label.textContent = state.promo.label || `−${Math.round(state.promo.percent)}%`;
  if (until) {
    if (state.promo.endsAt) {
      const d = new Date(state.promo.endsAt);
      until.textContent = Number.isNaN(d.getTime())
        ? ""
        : `до ${d.toLocaleDateString("ru-RU", { day: "numeric", month: "long" })}`;
    } else until.textContent = "";
  }
}

function buildLoyaltyStrip() {
  const host = $("loyaltyStrip");
  if (!host) return;
  if (!state.depositBonus || !state.depositBonus.tiers.length) {
    host.hidden = true;
    host.innerHTML = "";
    document.body.classList.remove("has-loyalty-strip");
    return;
  }
  host.hidden = false;
  document.body.classList.add("has-loyalty-strip");
  const tiers = state.depositBonus.tiers
    .map(
      (t, i) => `
      <div class="loyalty-strip__tier" style="--i:${i}">
        <span class="loyalty-strip__from">от <b>${nf.format(t.minAmount)}</b> ${state.currency}</span>
        <span class="loyalty-strip__bonus">+${nf.format(t.bonusAmount)} ${state.currency}</span>
      </div>`,
    )
    .join("");
  host.innerHTML = `
    <div class="loyalty-strip__head">
      <svg class="loyalty-strip__ico"><use href="#i-wallet" /></svg>
      <div>
        <h2>Бонус за пополнение</h2>
        <p>Пополни баланс — получи бонус на счёт</p>
      </div>
    </div>
    <div class="loyalty-strip__tiers" style="--n:${state.depositBonus.tiers.length}">${tiers}</div>`;
}

function buildExtras() {
  const host = $("extras");
  if (!host) return;
  if (!state.extras || !state.extras.items.length) {
    host.hidden = true;
    host.innerHTML = "";
    document.body.classList.remove("has-extras");
    return;
  }
  host.hidden = false;
  document.body.classList.add("has-extras");
  const { title, items } = state.extras;
  host.innerHTML = `
    <div class="extras__head">
      <span class="extras__mark"></span>
      <h2 class="extras__title">${title}</h2>
    </div>
    <div class="extras__items" style="--n:${items.length}">
      ${items
        .map(
          (item, i) => `
        <div class="extras__item" style="--i:${i}">
          <span class="extras__label">
            <b>${item.label}</b>
            ${item.note ? `<em>${item.note}</em>` : ""}
          </span>
          <span class="extras__price" data-value="${item.price}">0<span>${state.currency}</span></span>
        </div>`,
        )
        .join("")}
    </div>`;
}

function countUpPrices() {
  const cells = document.querySelectorAll(".row__price, .extras__price");
  const duration = firstPaint ? 900 : 0;
  cells.forEach((cell, index) => {
    const target = Number(cell.dataset.value || 0);
    const unitEl = cell.querySelector("span");
    const unit = unitEl ? unitEl.outerHTML : `<span>${state.currency}</span>`;
    if (!duration) {
      cell.innerHTML = nf.format(target) + unit;
      return;
    }
    const start = performance.now() + index * 45;
    const tick = (now) => {
      const t = Math.min(1, Math.max(0, (now - start) / duration));
      const eased = 1 - Math.pow(1 - t, 3);
      cell.innerHTML = nf.format(Math.round(target * eased)) + unit;
      if (t < 1) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });
  firstPaint = false;
}

function renderBoard() {
  buildPromoBadge();
  buildCards();
  buildLoyaltyStrip();
  buildExtras();
  countUpPrices();
  applyPeriod();
  buildTicker();
  startSpotlight();
}

function currentPeriod(date = new Date()) {
  const h = date.getHours();
  if (h >= state.nightFrom || h < state.nightTo) return "night";
  if (h >= state.dayFrom && h < state.dayTo) return "day";
  return "hour";
}

function periodTitle(key) {
  if (key === "night") return "Ночной пакет";
  if (key === "day") return "Дневной пакет";
  return "Почасовая игра";
}

function applyPeriod() {
  $("clockMeta").textContent = periodTitle(currentPeriod());
}

function tickClock() {
  const now = new Date();
  $("clockHH").textContent = String(now.getHours()).padStart(2, "0");
  $("clockMM").textContent = String(now.getMinutes()).padStart(2, "0");
}

function startSpotlight() {
  window.clearInterval(spotlightTimer);
  const cards = [...document.querySelectorAll(".card")];
  if (!cards.length) return;
  let i = 0;
  spotlightTimer = window.setInterval(() => {
    cards.forEach((c) => c.classList.remove("is-spot"));
    cards[i % cards.length].classList.add("is-spot");
    i += 1;
  }, 6000);
}

function buildTicker() {
  const items = [];
  if (state.promo) items.push(`${state.promo.title || "Акция"}: ${state.promo.label} на все тарифы`);
  if (state.depositBonus && state.depositBonus.tiers.length) {
    items.push(
      "Бонус за пополнение: " +
        state.depositBonus.tiers
          .map((t) => `от ${nf.format(t.minAmount)} → +${nf.format(t.bonusAmount)}`)
          .join(" · "),
    );
  }
  items.push(
    "Комфортная атмосфера",
    "Быстрый интернет",
    "Топовая периферия",
    "Мощное железо",
    "Пакеты 2+1 и 3+2 выгоднее почасовой игры",
    `Ночь ${String(state.nightFrom).padStart(2, "0")}:00 — ${String(state.nightTo).padStart(2, "0")}:00 по фиксированной цене`,
  );
  if (club.address) items.push(club.address);
  if (club.botUsername) items.push(`Telegram: @${club.botUsername}`);
  const html = items.map((t) => `<span><i>◆</i>${t}</span>`).join("");
  $("tickerTrack").innerHTML = html + html;
}

async function loadClub() {
  try {
    const res = await fetch("/api/public/club", { cache: "no-store" });
    const json = await res.json();
    const d = json && json.data;
    if (!d) return;
    club.botUsername = (d.botUsername || "").replace(/^@/, "");
    club.address = (d.address || "").trim();
    club.city = d.city || "";
    buildTicker();
  } catch {
    /* ignore */
  }
}

async function loadPrice(opts = {}) {
  try {
    const res = await fetch("/api/public/price", { cache: "no-store" });
    const json = await res.json();
    if (!json || !json.success || !json.data) throw new Error("bad payload");
    const prev = JSON.stringify({
      z: state.zones,
      e: state.extras,
      p: state.promo,
      d: state.depositBonus,
      w: [state.dayFrom, state.dayTo, state.nightFrom, state.nightTo],
    });
    if (!applyBoard(json.data)) throw new Error("empty zones");
    const next = JSON.stringify({
      z: state.zones,
      e: state.extras,
      p: state.promo,
      d: state.depositBonus,
      w: [state.dayFrom, state.dayTo, state.nightFrom, state.nightTo],
    });
    if (opts.force || prev !== next) renderBoard();
    return true;
  } catch {
    if (opts.force || state.source !== "api") {
      applyBoard({
        currency: FALLBACK.currency,
        zones: FALLBACK.zones,
        extras: FALLBACK.extras,
        windows: {
          dayFromHour: FALLBACK.dayFrom,
          dayToHour: FALLBACK.dayTo,
          nightFromHour: FALLBACK.nightFrom,
          nightToHour: FALLBACK.nightTo,
        },
      });
      state.source = "fallback";
      renderBoard();
    }
    return false;
  }
}

function syncPortraitLayout() {
  const narrow = window.innerWidth < 1280 || window.innerHeight + 40 >= window.innerWidth;
  document.body.classList.toggle("is-portrait", narrow);
}

function kioskBehaviour() {
  let idle;
  const wake = () => {
    document.body.classList.remove("is-idle");
    clearTimeout(idle);
    idle = setTimeout(() => document.body.classList.add("is-idle"), 3000);
  };
  ["mousemove", "keydown", "touchstart"].forEach((e) => window.addEventListener(e, wake));
  wake();

  document.addEventListener("dblclick", () => {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen?.();
  });

  window.addEventListener("keydown", (e) => {
    if (e.key !== "Enter" && e.code !== "NumpadEnter") return;
    if (e.repeat) return;
    e.preventDefault();
    cycleTheme(1);
  });

  window.addEventListener("resize", syncPortraitLayout);
  window.addEventListener("orientationchange", syncPortraitLayout);
  setTimeout(() => location.reload(), 6 * 60 * 60 * 1000);
}

document.querySelectorAll(".title span").forEach((el, n) => el.style.setProperty("--n", String(n)));

syncPortraitLayout();
themeIndex = readStoredThemeIndex();
THEMES.forEach((t) => document.body.classList.remove(`theme-${t.id}`));
document.body.classList.add(`theme-${THEMES[themeIndex].id}`);
{
  const theme = THEMES[themeIndex];
  const nameEl = $("themeName");
  const idxEl = $("themeIndex");
  if (nameEl) nameEl.textContent = theme.name;
  if (idxEl) idxEl.textContent = `${themeIndex + 1} / ${THEMES.length}`;
}

applyBoard({
  currency: FALLBACK.currency,
  zones: FALLBACK.zones,
  extras: FALLBACK.extras,
  windows: {
    dayFromHour: FALLBACK.dayFrom,
    dayToHour: FALLBACK.dayTo,
    nightFromHour: FALLBACK.nightFrom,
    nightToHour: FALLBACK.nightTo,
  },
});
renderBoard();
tickClock();
kioskBehaviour();

void (async () => {
  await loadPrice({ force: true });
  await loadClub();
})();

setInterval(tickClock, 1000);
setInterval(applyPeriod, 30000);
setInterval(() => void loadPrice(), 60 * 1000);
