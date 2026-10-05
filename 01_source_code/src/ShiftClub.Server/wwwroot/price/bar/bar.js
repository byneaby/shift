/* SHIFT Cyber Club — TV-меню бара.
   Данные: /api/public/bar (панель → БД). FALLBACK — если API недоступен. */

const FALLBACK = {
  currency: "T",
  categories: [
    {
      id: "drinks",
      code: "DRINKS",
      name: "НАПИТКИ",
      color: "#17a3ff",
      items: [
        { id: "w", sku: "WATER05", name: "Вода 0.5л", note: "", price: 150, imageUrl: null },
        { id: "c", sku: "COLA033", name: "Cola 0.33л", note: "", price: 350, imageUrl: null },
      ],
    },
    {
      id: "energy",
      code: "ENERGY",
      name: "ЭНЕРГЕТИКИ",
      color: "#ff6a00",
      items: [
        { id: "rb", sku: "RBULL", name: "Red Bull", note: "", price: 700, imageUrl: null },
      ],
    },
    {
      id: "snacks",
      code: "SNACKS",
      name: "СНЕКИ",
      color: "#ffc021",
      items: [
        { id: "ch", sku: "CHIPS", name: "Чипсы", note: "", price: 450, imageUrl: null },
      ],
    },
    {
      id: "hookah",
      code: "HOOKAH",
      name: "КАЛЬЯН",
      color: "#a78bfa",
      items: [
        { id: "hl", sku: "HOOKAH_LIGHT", name: "Лайт", note: "мягкий микс", price: 3500, imageUrl: null },
        { id: "hh", sku: "HOOKAH_HARD", name: "Хард", note: "крепкий микс", price: 6500, imageUrl: null },
        { id: "hb", sku: "HOOKAH_BOWL", name: "Замена чаши", note: "", price: 2000, imageUrl: null },
      ],
    },
  ],
};

const CATEGORY_ICON = {
  DRINKS: "i-cup",
  DRINK: "i-cup",
  ENERGY: "i-bolt",
  ENERGIES: "i-bolt",
  SNACKS: "i-snack",
  SNACK: "i-snack",
  HOOKAH: "i-hookah",
};

const state = {
  currency: FALLBACK.currency,
  categories: FALLBACK.categories,
  source: "fallback",
};

const nf = new Intl.NumberFormat("ru-RU");
const $ = (id) => document.getElementById(id);
const club = { botUsername: "", address: "", city: "" };

let spotlightTimer = 0;
let focusTimer = 0;
let firstPaint = true;

function esc(s) {
  return String(s || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function categoryIcon(code) {
  return CATEGORY_ICON[(code || "").toUpperCase()] || "i-info";
}

function applyMenu(data) {
  if (!data || !Array.isArray(data.categories) || data.categories.length === 0) return false;
  state.currency = data.currency || "T";
  state.categories = data.categories.map((c) => ({
    id: c.id || c.code || c.name,
    code: c.code || "",
    name: c.name,
    color: c.color || "#9aa4b2",
    items: (c.items || []).map((it) => ({
      id: it.id || it.sku,
      sku: it.sku || "",
      name: it.name,
      note: it.note || "",
      price: Number(it.price) || 0,
      imageUrl: it.imageUrl || null,
    })),
  }));
  state.source = "api";
  return true;
}

function buildCards() {
  const host = $("cards");
  host.innerHTML = "";
  const n = Math.max(1, state.categories.length);
  const portrait = document.body.classList.contains("is-portrait");
  // Landscape: side-by-side columns. Portrait: single stacked column.
  const cols = portrait ? 1 : Math.min(4, n);
  host.style.setProperty("--cols", String(cols));
  host.style.setProperty("--cats", String(n));

  state.categories.forEach((cat, i) => {
    const card = document.createElement("article");
    card.className = "card";
    card.dataset.cat = cat.id;
    card.style.setProperty("--c", cat.color);
    card.style.setProperty("--i", String(i));

    const compact = cat.items.length <= 3;
    const ico = categoryIcon(cat.code);
    const rows = cat.items
      .map((item, r) => {
        const hasImg = !!item.imageUrl;
        const media = hasImg
          ? `<img class="row__thumb" src="${esc(item.imageUrl)}" alt="" loading="lazy" />`
          : `<svg class="row__ico"><use href="#${ico}" /></svg>`;
        return `
        <div class="row${hasImg ? "" : " row--noimg"}" data-sku="${esc(item.sku)}" style="--r:${r}">
          ${media}
          <span class="row__label">
            <b>${esc(item.name)}</b>
            ${item.note ? `<em>${esc(item.note)}</em>` : ""}
          </span>
          <span class="row__price" data-value="${item.price}">0<span>${esc(state.currency)}</span></span>
        </div>`;
      })
      .join("");

    card.innerHTML = `
      <div class="card__inner${compact ? " card__inner--compact" : ""}">
        <header class="card__head">
          <h2 class="card__name">${esc(cat.name)}</h2>
          <span class="card__slashes"><i style="--s:0"></i><i style="--s:1"></i><i style="--s:2"></i></span>
        </header>
        <div class="card__rows" style="--rowcount:${Math.max(1, cat.items.length)}">${rows}</div>
      </div>`;
    host.appendChild(card);
  });
}

function countUpPrices() {
  const cells = document.querySelectorAll(".row__price");
  const duration = firstPaint ? 900 : 0;
  cells.forEach((cell, index) => {
    const target = Number(cell.dataset.value || 0);
    const unitEl = cell.querySelector("span");
    const unit = unitEl ? unitEl.outerHTML : `<span>${state.currency}</span>`;
    if (!duration) {
      cell.innerHTML = nf.format(target) + unit;
      return;
    }
    const start = performance.now() + index * 40;
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

function startRowFocus() {
  window.clearInterval(focusTimer);
  const rows = [...document.querySelectorAll(".row")];
  if (!rows.length) return;
  let i = 0;
  focusTimer = window.setInterval(() => {
    rows.forEach((r) => r.classList.remove("is-focus"));
    rows[i % rows.length].classList.add("is-focus");
    i += 1;
  }, 2800);
}

function buildTicker() {
  const items = [
    "Закажи у администратора или с ПК",
    "Энергетики · напитки · снеки · кальян",
    "Доставка к месту",
  ];
  if (club.address) items.push(club.address);
  if (club.botUsername) items.push(`Telegram: @${club.botUsername}`);
  const html = items.map((t) => `<span><i>◆</i>${esc(t)}</span>`).join("");
  $("tickerTrack").innerHTML = html + html;
}

function renderBoard() {
  buildCards();
  countUpPrices();
  buildTicker();
  startSpotlight();
  startRowFocus();
}

function tickClock() {
  const now = new Date();
  $("clockHH").textContent = String(now.getHours()).padStart(2, "0");
  $("clockMM").textContent = String(now.getMinutes()).padStart(2, "0");
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

async function loadMenu(opts = {}) {
  try {
    const res = await fetch("/api/public/bar", { cache: "no-store" });
    const json = await res.json();
    if (!json || !json.success || !json.data) throw new Error("bad payload");
    const prev = JSON.stringify(state.categories);
    if (!applyMenu(json.data)) throw new Error("empty categories");
    const next = JSON.stringify(state.categories);
    if (opts.force || prev !== next) renderBoard();
    return true;
  } catch {
    if (opts.force || state.source !== "api") {
      applyMenu(FALLBACK);
      state.source = "fallback";
      renderBoard();
    }
    return false;
  }
}

function syncPortraitLayout() {
  const narrow = window.innerWidth < 1280 || window.innerHeight + 40 >= window.innerWidth;
  const was = document.body.classList.contains("is-portrait");
  document.body.classList.toggle("is-portrait", narrow);
  if (was !== narrow && state.categories.length) {
    // Columns vs stack — rebuild so --cols/--cats match orientation.
    firstPaint = false;
    renderBoard();
  }
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

  window.addEventListener("resize", syncPortraitLayout);
  window.addEventListener("orientationchange", syncPortraitLayout);
  setTimeout(() => location.reload(), 6 * 60 * 60 * 1000);
}

document.querySelectorAll(".title span").forEach((el, n) => el.style.setProperty("--n", String(n)));

syncPortraitLayout();
applyMenu(FALLBACK);
renderBoard();
tickClock();
kioskBehaviour();

void (async () => {
  await loadMenu({ force: true });
  await loadClub();
})();

setInterval(tickClock, 1000);
setInterval(() => void loadMenu(), 30000);
