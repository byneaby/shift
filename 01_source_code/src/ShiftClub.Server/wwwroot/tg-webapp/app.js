(() => {
  const tg = window.Telegram && window.Telegram.WebApp;

  function syncTgViewport() {
    const h =
      (tg && (tg.viewportStableHeight || tg.viewportHeight)) ||
      window.innerHeight ||
      0;
    if (h > 0) {
      document.documentElement.style.setProperty("--tg-vh", `${Math.round(h)}px`);
    }
  }

  if (tg) {
    tg.ready();
    tg.expand();
    try {
      tg.setHeaderColor("#0e0e10");
      tg.setBackgroundColor("#0e0e10");
    } catch (_) {}
    syncTgViewport();
    try {
      tg.onEvent("viewportChanged", syncTgViewport);
    } catch (_) {}
  }
  window.addEventListener("resize", syncTgViewport);

  const API = "";
  const TOKEN_KEY = "shift.tg.token";
  const PANEL_KEY = "shift.tg.panel";

  const params = new URLSearchParams(location.search || "");
  const startParam = (tg && tg.initDataUnsafe && tg.initDataUnsafe.start_param) || params.get("panel") || "";
  const startParamLc = String(startParam).toLowerCase();
  const wantsCase =
    /upgrade|case|promo|кейс/.test(startParamLc) || params.get("case") === "1";
  const initialPanel =
    localStorage.getItem(PANEL_KEY) ||
    (startParamLc.includes("staff") ? "staff" : "client");

  const state = {
    panel: initialPanel === "staff" ? "staff" : "client",
    tab: "home",
    auth: null,
    home: null,
    staffHome: null,
    floorMap: null,
    floorView: null, // 'zones' | 'map' — null = авто
    floorZoom: null, // null = авто «в экран»
    loyalty: null,
    caseState: null,
    lastCaseWin: null,
    pendingCaseOpen: wantsCase,
    justRegistered: false,
    catalog: [],
    cart: {},
    flash: "",
    error: "",
  };

  const el = document.getElementById("app");

  function money(n) {
    const v = Number(n) || 0;
    return `${v.toFixed(0)} ₸`;
  }

  /** минуты → «1 ч 20 мин» / «45 мин» */
  function fmtDuration(mins) {
    const m = Math.max(0, Math.round(Number(mins) || 0));
    if (m < 60) return `${m} мин`;
    const h = Math.floor(m / 60);
    const r = m % 60;
    return r === 0 ? `${h} ч` : `${h} ч ${r} мин`;
  }

  function fmtSeconds(sec) {
    if (sec == null) return "—";
    return fmtDuration(Math.ceil(Number(sec) / 60));
  }

  function profileOf() {
    return (state.home && state.home.profile) || state.auth || {};
  }

  function renderLoyaltyBlock(p) {
    const level = p.loyaltyLevelName || "Без уровня";
    const goal = Number(p.loyaltyGoal) || 0;
    const progress = Number(p.loyaltyProgress) || 0;
    const pct = goal > 0 ? Math.min(100, Math.round((progress / goal) * 100)) : 0;
    const bonus = Number(p.loyaltyBonusPercent) || 0;
    const disc = Number(p.loyaltyTimeDiscountPercent) || 0;
    const perks = [
      bonus > 0 ? `бонус ${bonus}%` : "",
      disc > 0 ? `скидка на время ${disc}%` : "",
    ]
      .filter(Boolean)
      .join(" · ");
    return `
      <div class="card">
        <h2>Лояльность</h2>
        <div class="row"><strong>${esc(level)}</strong><span class="chip">${pct}%</span></div>
        <div class="loyalty-bar"><i style="width:${pct}%"></i></div>
        <div class="muted" style="margin-top:8px;font-size:.82rem">
          ${goal > 0 ? `Прогресс: ${money(progress)} из ${money(goal)}` : "Уровни не настроены"}
          ${perks ? ` · ${esc(perks)}` : ""}
        </div>
        <div class="grid2" style="margin-top:12px">
          <div class="stat"><div class="l">Визиты</div><div class="v">${p.visitCount || 0}</div></div>
          <div class="stat"><div class="l">Серия</div><div class="v">${p.visitStreakDays || 0} дн.</div></div>
        </div>
        ${
          Number(p.pendingBarRewards) > 0
            ? `<div class="ok" style="margin-top:10px">Награда: напиток ×${Number(p.pendingBarRewards)} — заберите на кассе</div>`
            : ""
        }
        <button class="btn ghost" id="openLoyalty" style="margin-top:12px">Все условия клуба</button>
      </div>`;
  }

  function renderBankBlock(p) {
    const banks = Array.isArray(p.timeBanks) ? p.timeBanks : [];
    const total = banks.reduce((a, b) => a + (Number(b.minutes) || 0), 0) || Number(p.timeBankMinutes) || 0;
    const curName = p.currentZoneName;
    const curMins = Number(p.currentZoneTimeBankMinutes) || 0;
    const rows =
      banks.length > 0
        ? banks
            .map((b) => {
              const isCur = p.currentZoneId && String(b.zoneId) === String(p.currentZoneId);
              return `<div class="bank-row ${isCur ? "is-cur" : ""}">
                <div>
                  <strong>${esc(b.zoneName || "Зона")}</strong>
                  ${isCur ? `<span class="chip" style="margin-left:6px">сейчас</span>` : ""}
                </div>
                <span>${fmtDuration(b.minutes)}</span>
              </div>`;
            })
            .join("")
        : `<div class="muted">Банк пуст — минуты появятся после сохранения остатка сеанса.</div>`;
    return `
      <div class="card">
        <h2>Банк времени</h2>
        <div class="money" style="font-size:1.25rem">${fmtDuration(total)}</div>
        <div class="muted" style="margin-top:6px;font-size:.82rem;line-height:1.4">
          Минуты привязаны к зоне: Standard нельзя потратить в VIP.
          ${
            curName
              ? ` На текущем ПК (${esc(curName)}): <strong>${fmtDuration(curMins)}</strong>.`
              : ""
          }
        </div>
        <div class="bank-list">${rows}</div>
      </div>`;
  }

  function token() {
    return localStorage.getItem(TOKEN_KEY) || (state.auth && state.auth.accessToken) || "";
  }

  async function api(path, opts = {}) {
    const headers = { "Content-Type": "application/json", ...(opts.headers || {}) };
    const t = token();
    if (t) headers.Authorization = `Bearer ${t}`;
    const res = await fetch(`${API}${path}`, { ...opts, headers });
    const json = await res.json().catch(() => ({}));
    if (!res.ok || json.success === false) {
      throw new Error(json.message || `Ошибка ${res.status}`);
    }
    return json.data;
  }

  function readInitData() {
    const fromApi = (tg && tg.initData) || "";
    if (fromApi) return fromApi;
    try {
      const hash = (location.hash || "").replace(/^#/, "");
      const p = new URLSearchParams(hash);
      const raw = p.get("tgWebAppData");
      if (raw) return decodeURIComponent(raw);
    } catch (_) {}
    return "";
  }

  async function waitInitData(ms) {
    const deadline = Date.now() + ms;
    while (Date.now() < deadline) {
      const d = readInitData();
      if (d) return d;
      await new Promise((r) => setTimeout(r, 40));
    }
    return readInitData();
  }

  function setPanel(panel) {
    state.panel = panel;
    state.tab = "home";
    state.flash = "";
    state.error = "";
    localStorage.setItem(PANEL_KEY, panel);
  }

  function hasCustomer(a) {
    const id = a && a.customerId;
    if (!id) return false;
    const s = String(id).toLowerCase();
    return s !== "00000000-0000-0000-0000-000000000000";
  }

  function needsClubAccount(a) {
    return !!(a && (a.needsRegistration || !hasCustomer(a)));
  }

  async function bootstrap() {
    try {
      if (tg) {
        try {
          tg.ready();
        } catch (_) {}
      }
      const initData = await waitInitData(1500);
      if (!initData) {
        renderBlocked(
          "Нет данных Telegram.\nОткройте через синюю кнопку «Открыть SHIFT» или SHIFT в меню бота."
        );
        return;
      }
      const auth = await api("/api/tg/auth", {
        method: "POST",
        body: JSON.stringify({ initData }),
      });
      state.auth = auth;
      localStorage.setItem(TOKEN_KEY, auth.accessToken);

      if (auth.isStaff && state.panel === "staff") {
        await loadStaff();
        render();
        return;
      }

      if (needsClubAccount(auth)) {
        if (auth.isStaff) {
          setPanel("staff");
          await loadStaff();
          render();
          return;
        }
        renderWelcome(initData);
        return;
      }

      setPanel("client");
      await loadHome();
      render();
    } catch (e) {
      renderBlocked(e.message || "Не удалось войти");
    }
  }

  async function loadHome() {
    state.home = await api("/api/tg/home");
    state.auth = state.home.profile;
    localStorage.setItem(TOKEN_KEY, state.auth.accessToken);
    try {
      state.floorMap = await api("/api/tg/floor-map");
    } catch (_) {
      /* map tab загрузит сама */
    }
  }

  async function loadStaff() {
    state.staffHome = await api("/api/tg/staff/home");
    state.auth = state.staffHome.profile;
    localStorage.setItem(TOKEN_KEY, state.auth.accessToken);
    try {
      state.floorMap = await api("/api/tg/floor-map");
    } catch (_) {
      state.floorMap = null;
    }
  }

  async function loadLoyalty() {
    // Публичный endpoint — без обязательной TG-сессии
    const res = await fetch("/api/public/loyalty");
    const json = await res.json().catch(() => ({}));
    if (!res.ok || json.success === false) {
      throw new Error(json.message || `Ошибка ${res.status}`);
    }
    state.loyalty = json.data;
  }

  async function loadCase() {
    state.caseState = await api("/api/tg/case");
  }

  async function loadFloorMap() {
    state.floorMap = await api("/api/tg/floor-map");
  }

  function renderBlocked(msg) {
    el.innerHTML = `<div class="center"><div class="brand">SHIFT</div><p style="margin-top:16px;white-space:pre-line">${esc(
      msg
    )}</p></div>`;
  }

  function esc(s) {
    return String(s || "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function barOrderStatus(status) {
    const s = String(status || "").trim();
    const map = {
      New: "Новый",
      Accepted: "Принят",
      Preparing: "Готовится",
      Ready: "Готов",
      Delivering: "Доставляется",
      Completed: "Выполнен",
      Done: "Выполнен",
      Cancelled: "Отменён",
      Canceled: "Отменён",
      Rejected: "Отклонён",
    };
    return map[s] || s || "Оформлен";
  }

  /** @returns {Promise<'save'|'discard'|'cancel'>} */
  function showThreeChoice(title, message, saveLabel, discardLabel) {
    return new Promise((resolve) => {
      const prev = document.getElementById("choiceSheet");
      if (prev) prev.remove();
      const wrap = document.createElement("div");
      wrap.id = "choiceSheet";
      wrap.className = "sheet-backdrop";
      wrap.innerHTML = `
        <div class="sheet" role="dialog" aria-modal="true">
          <h3>${esc(title)}</h3>
          <p>${esc(message)}</p>
          <button type="button" class="btn" data-c="save">${esc(saveLabel)}</button>
          <button type="button" class="btn ghost" data-c="discard">${esc(discardLabel)}</button>
          <button type="button" class="btn ghost" data-c="cancel">Отмена</button>
        </div>`;
      const finish = (v) => {
        wrap.remove();
        resolve(v);
      };
      wrap.addEventListener("click", (e) => {
        if (e.target === wrap) finish("cancel");
      });
      wrap.querySelectorAll("[data-c]").forEach((b) => {
        b.addEventListener("click", () => finish(b.getAttribute("data-c")));
      });
      document.body.appendChild(wrap);
    });
  }

  function modeSwitch() {
    const a = state.auth || {};
    if (!a.isStaff) return "";
    return `<div class="mode-switch">
      <button class="${state.panel === "client" ? "on" : ""}" data-panel="client">Клиент</button>
      <button class="${state.panel === "staff" ? "on" : ""}" data-panel="staff">Сотрудник</button>
    </div>`;
  }

  function renderWelcome(initData) {
    const name = (state.auth && state.auth.fullName) || "гость";
    el.innerHTML = `
      <div class="header">
        <div class="brand">SHIFT Club</div>
        <h1>Добро пожаловать</h1>
        <div class="sub">Привет, ${esc(name)}. Telegram ещё не связан с аккаунтом клуба.</div>
      </div>
      <div class="card welcome-card">
        <h2>Что можно сделать</h2>
        <p class="muted" style="margin:0 0 14px;line-height:1.45">
          Аккаунт клуба — это ваш баланс, сеансы на ПК, бар и лояльность.
          После создания или привязки откроются все разделы приложения.
        </p>
        <button class="btn" id="goCreateAccount">Создать новый аккаунт</button>
        <button class="btn ghost" id="goLinkAccount">У меня уже есть аккаунт</button>
        <button class="btn ghost" id="welcomeScanQr">Сканировать QR на ПК</button>
      </div>
      <div class="card">
        <h2>Или введите код с экрана</h2>
        <p class="muted" style="margin:0 0 8px;font-size:.85rem">Код под QR на экране ПК (вход / привязка)</p>
        <label>Код<input id="manualCode" placeholder="например AB12CD" inputmode="text" maxlength="16" /></label>
        <div class="err" id="welcomeErr"></div>
        <div class="ok" id="welcomeOk"></div>
        <button class="btn ghost" id="welcomeCodeBtn" style="margin-top:12px">Подтвердить код</button>
      </div>`;

    document.getElementById("goCreateAccount").onclick = () => renderRegister(initData || readInitData());
    document.getElementById("goLinkAccount").onclick = () => renderLink(initData || readInitData());
    document.getElementById("welcomeScanQr").onclick = () => startQrScan({ stayOpen: true });
    document.getElementById("welcomeCodeBtn").onclick = async () => {
      const err = document.getElementById("welcomeErr");
      const ok = document.getElementById("welcomeOk");
      err.textContent = "";
      ok.textContent = "";
      try {
        await confirmQrPayload(document.getElementById("manualCode").value.trim(), { initData });
      } catch (e) {
        err.textContent = e.message;
      }
    };
  }

  function renderLink(initData) {
    el.innerHTML = `
      <div class="header">
        <div class="brand">SHIFT Club</div>
        <h1>Привязать аккаунт</h1>
        <div class="sub">Телефон или логин и пароль — как при входе на ПК</div>
      </div>
      <div class="card">
        <label>Телефон или логин<input id="linkLogin" placeholder="77001234567" autocomplete="username" /></label>
        <label>Пароль или ПИН<input id="linkPass" type="password" placeholder="пароль с ПК" autocomplete="current-password" /></label>
        <div class="err" id="linkErr"></div>
        <button class="btn" id="linkBtn" style="margin-top:16px">Привязать Telegram</button>
        <button class="btn ghost" id="linkBack">Назад</button>
        <button class="btn ghost" id="linkScan">Сканировать QR привязки на ПК</button>
      </div>`;
    document.getElementById("linkBack").onclick = () => renderWelcome(initData || readInitData());
    document.getElementById("linkScan").onclick = () => startQrScan({ stayOpen: true });
    document.getElementById("linkBtn").onclick = async () => {
      const err = document.getElementById("linkErr");
      err.textContent = "";
      try {
        const auth = await api("/api/tg/link", {
          method: "POST",
          body: JSON.stringify({
            initData: initData || readInitData(),
            phoneOrLogin: document.getElementById("linkLogin").value,
            password: document.getElementById("linkPass").value,
          }),
        });
        await enterAfterAuth(auth, "Telegram привязан к аккаунту клуба");
      } catch (e) {
        err.textContent = e.message;
      }
    };
  }

  function renderRegister(initData) {
    el.innerHTML = `
      <div class="header">
        <div class="brand">SHIFT Club</div>
        <h1>Регистрация</h1>
        <div class="sub">Создайте аккаунт клуба. Эти же данные подойдут для входа на ПК.</div>
      </div>
      <div class="card">
        <label>Имя<input id="first" placeholder="Имя" autocomplete="given-name" /></label>
        <label>Фамилия<input id="last" placeholder="Фамилия" autocomplete="family-name" /></label>
        <label>Телефон<input id="phone" placeholder="77001234567" inputmode="tel" autocomplete="tel" /></label>
        <label>Пароль для ПК<input id="password" type="password" placeholder="минимум 4 символа" autocomplete="new-password" /></label>
        <label>ИИН <span class="muted">(необязательно)</span><input id="iin" placeholder="12 цифр" inputmode="numeric" maxlength="12" /></label>
        <p class="muted" style="margin:12px 0 0;font-size:.82rem;line-height:1.4">
          Если аккаунт с этим телефоном уже есть — введите его пароль: Telegram привяжется к нему.
        </p>
        <div class="err" id="regErr"></div>
        <button class="btn" id="regBtn" style="margin-top:16px">Создать аккаунт</button>
        <button class="btn ghost" id="regLink">У меня уже есть аккаунт</button>
        <button class="btn ghost" id="regBack">Назад</button>
      </div>`;
    document.getElementById("regBack").onclick = () => renderWelcome(initData || readInitData());
    document.getElementById("regLink").onclick = () => renderLink(initData || readInitData());
    document.getElementById("regBtn").onclick = async () => {
      const err = document.getElementById("regErr");
      err.textContent = "";
      const phone = (document.getElementById("phone").value || "").replace(/\D/g, "");
      if (phone.length < 10) {
        err.textContent = "Укажите телефон — не менее 10 цифр.";
        return;
      }
      if ((document.getElementById("password").value || "").trim().length < 4) {
        err.textContent = "Пароль: минимум 4 символа.";
        return;
      }
      if (!(document.getElementById("first").value || "").trim()) {
        err.textContent = "Укажите имя.";
        return;
      }
      if (!(document.getElementById("last").value || "").trim()) {
        err.textContent = "Укажите фамилию.";
        return;
      }
      try {
        const auth = await api("/api/tg/register", {
          method: "POST",
          body: JSON.stringify({
            initData: initData || readInitData(),
            phone: document.getElementById("phone").value,
            firstName: document.getElementById("first").value,
            lastName: document.getElementById("last").value,
            password: document.getElementById("password").value,
            iin: document.getElementById("iin").value,
          }),
        });
        state.justRegistered = true;
        state.pendingCaseOpen = true;
        await enterAfterAuth(auth, "Аккаунт создан — кейс на кассе");
      } catch (e) {
        err.textContent = e.message;
      }
    };
  }

  async function enterAfterAuth(auth, flash) {
    state.auth = auth;
    localStorage.setItem(TOKEN_KEY, auth.accessToken);
    state.flash = flash || "";
    state.error = "";
    if (needsClubAccount(auth)) {
      renderWelcome(readInitData());
      return;
    }
    setPanel("client");
    await loadHome();
    if (state.pendingCaseOpen || state.justRegistered) {
      state.tab = "case";
      state.flash =
        state.justRegistered
          ? "Аккаунт создан! Ключ на кейс есть — подойди к кассе, там откроют рулетку."
          : "SHIFT CASE открывается только на кассе.";
      try {
        await loadCase();
      } catch (e) {
        state.error = e.message;
        state.tab = "home";
      }
      state.pendingCaseOpen = false;
      state.justRegistered = false;
    } else {
      state.tab = "home";
    }
    render();
  }

  function navClient() {
    const tabs = [
      ["home", "🏠", "Главная"],
      ["map", "🗺", "Зал"],
      ["case", "🎁", "Кейс"],
      ["account", "👤", "Профиль"],
    ];
    const left = tabs.slice(0, 2);
    const right = tabs.slice(2);
    const btn = ([id, ico, label]) =>
      `<button class="${state.tab === id ? "on" : ""}" data-tab="${id}"><span class="ico">${ico}</span>${label}</button>`;
    return `<nav class="nav nav-main">
      ${left.map(btn).join("")}
      <button class="nav-qr" type="button" id="navQr" aria-label="Сканировать QR">
        <span class="nav-qr__icon" aria-hidden="true">
          <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3"/>
            <rect x="6" y="6" width="5" height="5" rx="1"/>
            <rect x="13" y="6" width="5" height="5" rx="1"/>
            <rect x="6" y="13" width="5" height="5" rx="1"/>
            <path d="M13 13h2v2h-2zM17 13h1v1h-1zM16 16h2v2h-2zM13 17h1v1h-1z"/>
          </svg>
        </span>
        <span class="nav-qr__label">QR</span>
      </button>
      ${right.map(btn).join("")}
    </nav>`;
  }

  function navStaff() {
    const tabs = [
      ["home", "🖥", "Зал"],
      ["bookings", "📅", "Брони"],
      ["bar", "🍹", "Бар"],
      ["shift", "💼", "Смена"],
    ];
    return `<nav class="nav">${tabs
      .map(
        ([id, ico, label]) =>
          `<button class="${state.tab === id ? "on" : ""}" data-tab="${id}"><span class="ico">${ico}</span>${label}</button>`
      )
      .join("")}</nav>`;
  }

  function render() {
    if (state.panel === "staff" && state.auth && state.auth.isStaff) {
      renderStaff();
      return;
    }
    renderClient();
  }

  function renderClient() {
    const p = { ...(state.auth || {}), ...((state.home && state.home.profile) || {}) };
    if (needsClubAccount(state.auth)) {
      renderWelcome(readInitData());
      return;
    }
    const bal = p.comfortHideBalance ? "•••" : money(p.balance);
    const bonus = p.comfortHideBalance ? "•••" : money(p.bonusBalance);
    let body = "";
    if (state.tab === "home") body = renderHome(p, bal, bonus);
    else if (state.tab === "map") body = renderFloorMap(false);
    else if (state.tab === "session") body = renderSession();
    else if (state.tab === "bar") body = renderBar();
    else if (state.tab === "case") body = renderCase();
    else if (state.tab === "loyalty") body = renderLoyalty();
    else body = renderAccount(p);

    el.innerHTML = modeSwitch() + body + navClient();
    el.classList.add("has-qr-nav");
    bindCommon();
    bindClientActions();
    bindFloorMap();
  }

  function renderStaff() {
    const p = state.auth || {};
    const s = state.staffHome;
    let body = "";
    if (state.tab === "home") body = renderStaffFloor(p, s);
    else if (state.tab === "bookings") body = renderStaffBookings(s);
    else if (state.tab === "bar") body = renderStaffBar(s);
    else if (state.tab === "shift") body = renderStaffShift(s);
    else body = renderStaffFloor(p, s);

    el.innerHTML = modeSwitch() + body + navStaff();
    el.classList.remove("has-qr-nav");
    bindCommon();
    bindFloorMap();
    bindClientActions();
    const refresh = document.getElementById("staffRefresh");
    if (refresh) {
      refresh.onclick = async () => {
        try {
          await loadStaff();
          render();
        } catch (e) {
          state.error = e.message;
          render();
        }
      };
    }
    el.querySelectorAll("[data-b-arrived]").forEach((b) => {
      b.onclick = async () => {
        try {
          await api(`/api/tg/staff/bookings/${b.getAttribute("data-b-arrived")}/arrived`, {
            method: "POST",
            body: "{}",
          });
          state.flash = "Гость отмечен как пришедший";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    });
    el.querySelectorAll("[data-b-cancel]").forEach((b) => {
      b.onclick = async () => {
        if (!confirm("Отменить бронь?")) return;
        try {
          await api(`/api/tg/staff/bookings/${b.getAttribute("data-b-cancel")}/cancel`, {
            method: "POST",
            body: JSON.stringify({ reason: "telegram-miniapp" }),
          });
          state.flash = "Бронь отменена";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    });
  }

  function isOfflineDetail(detail) {
    const d = String(detail || "").toLowerCase();
    return d.includes("офлайн") || d.includes("offline");
  }

  function isPausedPc(pc) {
    const occ = String(pc.occupancy || "").toLowerCase();
    const d = String(pc.occupancyDetail || "").toLowerCase();
    return occ === "paused" || d === "пауза" || d.includes("пауза");
  }

  function isOfflineFreePc(pc) {
    if (pc.sessionId) return false;
    const occ = String(pc.occupancy || "").toLowerCase();
    if (occ === "offline") return true;
    if (occ !== "free") return false;
    return isOfflineDetail(pc.occupancyDetail);
  }

  function isConsolePc(pc) {
    const k = String(pc.stationKind || "").toLowerCase();
    return k === "console" || k === "1";
  }

  function fmtClockIso(iso) {
    if (!iso) return "";
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return "";
    return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
  }

  /** Кассирский вид плитки: Свободен / Выкл / Занят / Пауза / Бронь */
  function pcView(pc) {
    const occ = String(pc.occupancy || "").toLowerCase();
    const offline = isOfflineFreePc(pc);
    const paused = isPausedPc(pc);
    let kind = occ;
    let label = "—";
    let css = "occ-offline";

    if (paused || occ === "busy") {
      kind = paused ? "paused" : "busy";
      label = paused ? "Пауза" : "Занят";
      css = "occ-busy";
    } else if (occ === "reserved" || (pc.bookingId && !pc.sessionId)) {
      kind = "reserved";
      label = "Бронь";
      css = "occ-reserved";
    } else if (occ === "maintenance" || occ === "updating" || occ === "setup") {
      kind = "maint";
      label = "ТО";
      css = "occ-maint";
    } else if (offline) {
      kind = "offline";
      label = "Выкл";
      css = "occ-offline";
    } else if (occ === "free") {
      kind = "free";
      label = "Свободен";
      css = "occ-free";
    } else if (occ === "offline") {
      kind = "offline";
      label = "Выкл";
      css = "occ-offline";
    }

    let metaExtra = "";
    if (pc.remainingMinutes != null && (kind === "busy" || kind === "paused")) {
      metaExtra = " · " + fmtDuration(pc.remainingMinutes);
    }
    if (pc.bookingStartsAt) {
      const range = fmtClockIso(pc.bookingStartsAt) + (pc.bookingEndsAt ? "–" + fmtClockIso(pc.bookingEndsAt) : "");
      if (range) {
        if (kind === "busy" || kind === "paused") metaExtra += ` · 📅${range}`;
        else if (kind === "reserved") metaExtra += " · " + range;
      }
    }

    return { kind, label, css, offline, paused, metaExtra, console: isConsolePc(pc) };
  }

  function occClass(o) {
    return pcView({ occupancy: o }).css;
  }

  function occLabel(o, detail) {
    return pcView({ occupancy: o, occupancyDetail: detail }).label;
  }

  function recountFloor(pcs) {
    const c = { free: 0, busy: 0, reserved: 0, offline: 0, maintenance: 0, total: (pcs || []).length };
    (pcs || []).forEach((pc) => {
      const v = pcView(pc);
      if (v.kind === "free") c.free++;
      else if (v.kind === "offline") c.offline++;
      else if (v.kind === "busy" || v.kind === "paused") c.busy++;
      else if (v.kind === "reserved") c.reserved++;
      else if (v.kind === "maint") c.maintenance++;
    });
    return c;
  }

  function isNarrowFloor() {
    return typeof window !== "undefined" && window.matchMedia && window.matchMedia("(max-width: 640px)").matches;
  }

  function floorViewMode() {
    if (state.floorView === "zones" || state.floorView === "map") return state.floorView;
    return isNarrowFloor() ? "zones" : "map";
  }

  function renderPcTile(pc, isStaff) {
    const view = pcView(pc);
    const rem = view.metaExtra;
    const guestBits = [];
    if (isStaff && pc.guestName) guestBits.push(pc.guestName);
    else if (isStaff && pc.occupancyDetail && !isOfflineDetail(pc.occupancyDetail) && !isPausedPc(pc))
      guestBits.push(pc.occupancyDetail);
    if (pc.bookingContact) guestBits.push("📅 " + pc.bookingContact);
    const detail = guestBits.join(" · ");
    const clickable = isStaff ? ` data-pc-id="${esc(pc.id)}"` : "";
    const border = pc.zoneColorHex ? `border-color:${esc(pc.zoneColorHex)};` : "";
    const badge = view.console ? `<span class="pc-tile__badge">PS5</span>` : "";
    return `<button type="button" class="pc-tile ${view.css}" style="${border}"${clickable}>
      <strong>${esc(pc.name)}${badge}</strong>
      <span class="pc-tile__meta">${esc(view.label)}${esc(rem)}</span>
      ${detail ? `<span class="pc-tile__guest">${esc(detail)}</span>` : ""}
      ${pc.zoneName ? `<span class="pc-tile__zone">${esc(pc.zoneName)}</span>` : ""}
    </button>`;
  }

  function renderFloorZones(pcs, isStaff) {
    const zones = {};
    pcs.forEach((pc) => {
      const z = (pc.zoneName && String(pc.zoneName).trim()) || "Без зоны";
      if (!zones[z]) zones[z] = [];
      zones[z].push(pc);
    });
    return Object.keys(zones)
      .sort((a, b) => {
        if (a === "Без зоны") return 1;
        if (b === "Без зоны") return -1;
        return a.localeCompare(b, "ru");
      })
      .map((z) => {
        const list = zones[z].slice().sort((a, b) => String(a.name).localeCompare(String(b.name), "ru"));
        const counts = recountFloor(list);
        const meta =
          counts.offline > 0
            ? `${counts.free} своб. · ${counts.offline} выкл · ${list.length} ПК`
            : `${counts.free} своб. · ${list.length} ПК`;
        return `<section class="zone-block">
          <div class="zone-head">
            <h2>${esc(z)}</h2>
            <span class="zone-meta">${meta}</span>
          </div>
          <div class="floor-grid-fallback">
            ${list.map((pc) => renderPcTile(pc, isStaff)).join("")}
          </div>
        </section>`;
      })
      .join("");
  }

  function isWallKind(kind) {
    const k = String(kind ?? "").toLowerCase();
    return k === "wall" || k === "6";
  }

  function computeFloorCrop(pcs, elements, gridCols, gridRows) {
    let minC = Infinity;
    let minR = Infinity;
    let maxC = -1;
    let maxR = -1;
    const touch = (c, r, cs, rs) => {
      const col = Number(c) || 0;
      const row = Number(r) || 0;
      const spanC = Math.max(1, Number(cs) || 1);
      const spanR = Math.max(1, Number(rs) || 1);
      minC = Math.min(minC, col);
      minR = Math.min(minR, row);
      maxC = Math.max(maxC, col + spanC - 1);
      maxR = Math.max(maxR, row + spanR - 1);
    };
    (pcs || []).forEach((p) => {
      if (p.gridCol == null || p.gridRow == null) return;
      touch(p.gridCol, p.gridRow, 1, 1);
    });
    (elements || []).forEach((e) => {
      if (isWallKind(e.kind)) return;
      if (e.gridCol == null || e.gridRow == null) return;
      touch(e.gridCol, e.gridRow, e.colSpan, e.rowSpan);
    });
    if (!Number.isFinite(minC) || maxC < 0) {
      return { originC: 0, originR: 0, cols: Math.max(1, gridCols), rows: Math.max(1, gridRows) };
    }
    const pad = 1;
    const originC = Math.max(0, minC - pad);
    const originR = Math.max(0, minR - pad);
    const endC = Math.min(gridCols - 1, maxC + pad);
    const endR = Math.min(gridRows - 1, maxR + pad);
    return {
      originC,
      originR,
      cols: Math.max(1, endC - originC + 1),
      rows: Math.max(1, endR - originR + 1),
    };
  }

  function renderFloorSpatial(m, pcs, elements, cols, rows, isStaff) {
    const visibleEls = (elements || []).filter((e) => !isWallKind(e.kind));
    const crop = computeFloorCrop(pcs, visibleEls, cols, rows);
    const cells = [];
    visibleEls.forEach((e) => {
      const col = (Number(e.gridCol) || 0) - crop.originC + 1;
      const row = (Number(e.gridRow) || 0) - crop.originR + 1;
      if (col < 1 || row < 1 || col > crop.cols || row > crop.rows) return;
      const cs = Math.min(Math.max(1, Number(e.colSpan) || 1), crop.cols - col + 1);
      const rs = Math.min(Math.max(1, Number(e.rowSpan) || 1), crop.rows - row + 1);
      cells.push(`<div class="floor-el" style="grid-column:${col}/span ${cs};grid-row:${row}/span ${rs};${
        e.colorHex ? `background:${esc(e.colorHex)}33;` : ""
      }">${esc(e.label || e.kind || "")}</div>`);
    });
    pcs.forEach((pc) => {
      if (pc.gridCol == null || pc.gridRow == null) return;
      const col = Number(pc.gridCol) - crop.originC + 1;
      const row = Number(pc.gridRow) - crop.originR + 1;
      if (col < 1 || row < 1 || col > crop.cols || row > crop.rows) return;
      const view = pcView(pc);
      const guestBits = [];
      if (isStaff && pc.guestName) guestBits.push(pc.guestName);
      else if (isStaff && pc.occupancyDetail && !isOfflineDetail(pc.occupancyDetail) && !isPausedPc(pc))
        guestBits.push(pc.occupancyDetail);
      if (pc.bookingContact) guestBits.push("📅 " + pc.bookingContact);
      const detail = guestBits.join(" · ");
      const clickable = isStaff ? ` data-pc-id="${esc(pc.id)}"` : "";
      cells.push(`<button type="button" class="pc-tile pc-tile--map ${view.css}" style="grid-column:${col};grid-row:${row};${
        pc.zoneColorHex ? `border-color:${esc(pc.zoneColorHex)};` : ""
      }"${clickable}>
        <strong>${esc(pc.name)}${view.console ? `<span class="pc-tile__badge">PS5</span>` : ""}</strong>
        <span class="pc-tile__meta">${esc(view.label)}${esc(view.metaExtra)}</span>
        ${detail ? `<span class="pc-tile__guest">${esc(detail)}</span>` : ""}
      </button>`);
    });
    const orphans = pcs.filter((p) => p.gridCol == null || p.gridRow == null);
    const z = Number(state.floorZoom) > 0 ? Number(state.floorZoom) : 1;
    const cellPx = window.matchMedia("(max-width: 420px)").matches ? 36 : 44;
    let html = `
      <div class="floor-zoom">
        <button type="button" class="floor-zoom__btn" id="floorZoomOut" aria-label="Мельче">−</button>
        <span id="floorZoomLabel">${Math.round(z * 100)}%</span>
        <button type="button" class="floor-zoom__btn" id="floorZoomIn" aria-label="Крупнее">+</button>
        <button type="button" class="floor-zoom__btn" id="floorZoomFit">В экран</button>
      </div>
      <div class="floor-viewport" id="floorScroll">
        <div class="floor-scaler" id="floorScaler" style="transform:scale(${z})">
          <div class="floor-board" id="floorBoard" style="--cols:${crop.cols};--rows:${crop.rows};--cell:${cellPx}px">${cells.join("")}</div>
        </div>
      </div>`;
    if (orphans.length) {
      html += `<div class="muted" style="margin-top:10px;font-size:.8rem">Без места на карте:</div>
        <div class="floor-grid-fallback" style="margin-top:8px">${orphans.map((pc) => renderPcTile(pc, isStaff)).join("")}</div>`;
    }
    return html;
  }

  function renderFloorMap(isStaff) {
    const m = state.floorMap;
    const pcs = (m && m.pcs) || [];
    const c = recountFloor(pcs);
    const elements = (m && m.elements) || [];
    const cols = Math.max(1, Number(m && m.gridCols) || 12);
    const rows = Math.max(1, Number(m && m.gridRows) || 8);
    const hasGrid = pcs.some((p) => p.gridCol != null && p.gridRow != null);
    const view = hasGrid ? floorViewMode() : "zones";

    let board = "";
    if (!m) {
      board = `<div class="muted">Загрузка…</div>`;
    } else if (!pcs.length) {
      board = `<div class="muted">ПК пока нет</div>`;
    } else if (view === "map" && hasGrid) {
      board = renderFloorSpatial(m, pcs, elements, cols, rows, isStaff);
    } else {
      board = renderFloorZones(pcs, isStaff);
    }

    const tabs = hasGrid
      ? `<div class="floor-tabs">
          <button type="button" class="${view === "zones" ? "on" : ""}" data-floor-view="zones">По зонам</button>
          <button type="button" class="${view === "map" ? "on" : ""}" data-floor-view="map">Карта</button>
        </div>`
      : "";

    const subBits = [`свободно ${c.free}`];
    if (c.offline) subBits.push(`выкл ${c.offline}`);
    subBits.push(`занято ${c.busy}`);
    if (c.reserved) subBits.push(`бронь ${c.reserved}`);
    subBits.push(`всего ${c.total || pcs.length}`);

    return `
      <div class="header">
        <div class="brand">SHIFT${isStaff ? " · Сотрудник" : ""}</div>
        <h1>Зал</h1>
        <div class="sub">${subBits.join(" · ")}${isStaff ? " · нажми ПК" : ""}</div>
      </div>
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      ${state.flash ? `<div class="card ok">${esc(state.flash)}</div>` : ""}
      <div class="card floor-stats">
        <div class="grid2">
          <div class="stat"><div class="l">Свободно</div><div class="v ok-v">${c.free}</div></div>
          <div class="stat"><div class="l">Выкл</div><div class="v">${c.offline}</div></div>
          <div class="stat"><div class="l">Сеанс</div><div class="v warn-v">${c.busy}</div></div>
          <div class="stat"><div class="l">Бронь</div><div class="v">${c.reserved}</div></div>
        </div>
        ${tabs}
        <button class="btn ghost" id="floorRefresh" style="margin-top:10px">Обновить</button>
      </div>
      <div class="card floor-card">${board}</div>`;
  }

  function bindFloorMap() {
    const btn = document.getElementById("floorRefresh");
    if (btn) {
      btn.onclick = async () => {
        try {
          state.error = "";
          if (state.panel === "staff") await loadStaff();
          else await loadFloorMap();
          render();
        } catch (e) {
          state.error = e.message;
          render();
        }
      };
    }

    el.querySelectorAll("[data-floor-view]").forEach((b) => {
      b.onclick = () => {
        state.floorView = b.getAttribute("data-floor-view");
        if (state.floorView === "map") state.floorZoom = null;
        render();
      };
    });

    const clampZoom = (z) => Math.min(2.4, Math.max(0.35, Math.round(z * 100) / 100));

    function applyFloorZoom(z) {
      state.floorZoom = clampZoom(z);
      const scaler = document.getElementById("floorScaler");
      const label = document.getElementById("floorZoomLabel");
      if (scaler) scaler.style.transform = `scale(${state.floorZoom})`;
      if (label) label.textContent = `${Math.round(state.floorZoom * 100)}%`;
    }

    function computeFitZoom() {
      const viewport = document.getElementById("floorScroll");
      const board = document.getElementById("floorBoard");
      if (!viewport || !board) return 1;
      const scaler = document.getElementById("floorScaler");
      if (scaler) scaler.style.transform = "scale(1)";
      const pad = 10;
      const availW = Math.max(120, viewport.clientWidth - pad);
      const availH = Math.max(120, viewport.clientHeight - pad);
      const needW = board.offsetWidth || 1;
      const needH = board.offsetHeight || 1;
      return clampZoom(Math.min(availW / needW, availH / needH));
    }

    function scheduleFloorFit() {
      if (!document.getElementById("floorBoard")) return;
      requestAnimationFrame(() => {
        applyFloorZoom(computeFitZoom());
      });
    }

    const zoomIn = document.getElementById("floorZoomIn");
    const zoomOut = document.getElementById("floorZoomOut");
    const zoomFit = document.getElementById("floorZoomFit");
    if (zoomIn) {
      zoomIn.onclick = () => {
        applyFloorZoom((Number(state.floorZoom) || 1) + 0.12);
      };
    }
    if (zoomOut) {
      zoomOut.onclick = () => {
        applyFloorZoom((Number(state.floorZoom) || 1) - 0.12);
      };
    }
    if (zoomFit) {
      zoomFit.onclick = () => scheduleFloorFit();
    }

    if (document.getElementById("floorBoard") && !(Number(state.floorZoom) > 0)) {
      scheduleFloorFit();
    } else if (document.getElementById("floorScaler") && Number(state.floorZoom) > 0) {
      applyFloorZoom(state.floorZoom);
    }

    el.querySelectorAll("[data-pc-id]").forEach((b) => {
      b.onclick = () => {
        if (state.panel !== "staff") return;
        const id = b.getAttribute("data-pc-id");
        openStaffPcSheet(id);
      };
    });
  }

  function findStaffPc(id) {
    const fromMap = ((state.floorMap && state.floorMap.pcs) || []).find((p) => String(p.id) === String(id));
    const fromStaff = ((state.staffHome && state.staffHome.floor && state.staffHome.floor.pcs) || []).find(
      (p) => String(p.id) === String(id)
    );
    return {
      ...(fromMap || {}),
      ...(fromStaff || {}),
      id: id,
      guestName: (fromStaff && fromStaff.guestName) || (fromMap && fromMap.guestName),
      bookingId: (fromStaff && fromStaff.bookingId) || (fromMap && fromMap.bookingId),
      bookingContact: (fromStaff && fromStaff.bookingContact) || (fromMap && fromMap.bookingContact),
      bookingStartsAt: (fromStaff && fromStaff.bookingStartsAt) || (fromMap && fromMap.bookingStartsAt),
      bookingEndsAt: (fromStaff && fromStaff.bookingEndsAt) || (fromMap && fromMap.bookingEndsAt),
      stationKind: (fromStaff && fromStaff.stationKind) || (fromMap && fromMap.stationKind),
      sessionId: (fromStaff && fromStaff.sessionId) || (fromMap && fromMap.sessionId),
    };
  }

  async function openStaffPcSheet(pcId) {
    const pc = findStaffPc(pcId);
    const view = pcView(pc);
    const freeLike = view.kind === "free" || view.kind === "offline" || view.kind === "reserved";
    const busy = view.kind === "busy" || view.kind === "paused";
    const rem =
      pc.remainingMinutes != null
        ? fmtDuration(pc.remainingMinutes)
        : pc.remainingSeconds != null
          ? fmtSeconds(pc.remainingSeconds)
          : "—";
    const bookLine =
      pc.bookingId && pc.bookingStartsAt
        ? `\n📅 ${pc.bookingContact || "Бронь"} · ${fmtClockIso(pc.bookingStartsAt)}${
            pc.bookingEndsAt ? "–" + fmtClockIso(pc.bookingEndsAt) : ""
          }`
        : "";

    const wrap = document.createElement("div");
    wrap.id = "staffPcSheet";
    wrap.className = "sheet-backdrop";
    wrap.innerHTML = `
      <div class="sheet sheet--tall" role="dialog">
        <h3>${esc(pc.name || "ПК")}${view.console ? " · PS5" : ""}</h3>
        <p>${esc(pc.zoneName || "")} · ${esc(view.label)}
          ${busy ? ` · осталось ${rem}` : ""}
          ${pc.guestName ? `\n${esc(pc.guestName)}` : ""}
          ${bookLine}
        </p>
        <div id="staffPcBody"></div>
        <button type="button" class="btn ghost" data-c="close">Закрыть</button>
      </div>`;
    document.body.appendChild(wrap);
    const close = () => wrap.remove();
    wrap.addEventListener("click", (e) => {
      if (e.target === wrap) close();
    });
    wrap.querySelector("[data-c=close]").onclick = close;

    const body = wrap.querySelector("#staffPcBody");
    const actions = [];

    if (freeLike) {
      actions.push(`<button class="btn" id="spStart">Запустить сеанс</button>`);
    }
    if (view.offline || view.kind === "offline" || (busy && isOfflineDetail(pc.occupancyDetail))) {
      actions.push(`<button class="btn ghost" id="spWake">Включить ПК (WOL)</button>`);
    }
    if (pc.bookingId && !busy) {
      actions.push(`<button class="btn ghost" id="spArrived">Гость пришёл</button>`);
      actions.push(`<button class="btn ghost" id="spCancelBook">Отменить бронь</button>`);
    }
    if (busy && pc.sessionId) {
      actions.push(`<button class="btn" id="spExtend">Продлить</button>`);
      actions.push(
        `<button class="btn ghost" id="spPause">${view.paused ? "Снять паузу" : "Пауза"}</button>`
      );
      actions.push(`<button class="btn ghost" id="spTransfer">Перенос на другой ПК</button>`);
      actions.push(`<button class="btn danger" id="spEnd">Завершить сеанс</button>`);
    }

    if (!actions.length) {
      body.innerHTML = `<div class="muted">Действий нет для статуса «${esc(view.label)}».</div>`;
      return;
    }
    body.innerHTML = actions.join("");

    const wakeBtn = body.querySelector("#spWake");
    if (wakeBtn) {
      wakeBtn.onclick = async () => {
        try {
          await api(`/api/tg/staff/computers/${pc.id}/wake`, { method: "POST", body: "{}" });
          close();
          state.flash = `Wake отправлен на ${pc.name}`;
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    }

    const startBtn = body.querySelector("#spStart");
    if (startBtn) {
      startBtn.onclick = () => {
        close();
        void staffStartWizard(pc);
      };
    }

    const arrivedBtn = body.querySelector("#spArrived");
    if (arrivedBtn) {
      arrivedBtn.onclick = async () => {
        try {
          await api(`/api/tg/staff/bookings/${pc.bookingId}/arrived`, { method: "POST", body: "{}" });
          close();
          state.flash = "Гость отмечен как пришедший";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    }

    const cancelBookBtn = body.querySelector("#spCancelBook");
    if (cancelBookBtn) {
      cancelBookBtn.onclick = async () => {
        if (!confirm("Отменить бронь?")) return;
        try {
          await api(`/api/tg/staff/bookings/${pc.bookingId}/cancel`, {
            method: "POST",
            body: JSON.stringify({ reason: "telegram-miniapp" }),
          });
          close();
          state.flash = "Бронь отменена";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    }

    const extendBtn = body.querySelector("#spExtend");
    if (extendBtn) {
      extendBtn.onclick = () => {
        close();
        void staffExtendWizard(pc);
      };
    }

    const transferBtn = body.querySelector("#spTransfer");
    if (transferBtn) {
      transferBtn.onclick = () => {
        close();
        void staffTransferWizard(pc);
      };
    }

    const pauseBtn = body.querySelector("#spPause");
    if (pauseBtn) {
      pauseBtn.onclick = async () => {
        try {
          if (view.paused) await api(`/api/tg/staff/sessions/${pc.sessionId}/resume`, { method: "POST", body: "{}" });
          else await api(`/api/tg/staff/sessions/${pc.sessionId}/pause`, { method: "POST", body: "{}" });
          close();
          state.flash = "Готово";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    }

    const endBtn = body.querySelector("#spEnd");
    if (endBtn) {
      endBtn.onclick = async () => {
        const choice = await showThreeChoice(
          "Завершить сеанс?",
          "Сохранить остаток минут в банк зоны гостя?",
          "Сохранить минуты",
          "Без сохранения"
        );
        if (choice === "cancel") return;
        try {
          await api(`/api/tg/staff/sessions/${pc.sessionId}/end`, {
            method: "POST",
            body: JSON.stringify({ saveRemainingToTimeBank: choice === "save" }),
          });
          close();
          state.flash = "Сеанс завершён";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    }
  }

  async function staffStartWizard(pc) {
    let tariffs = [];
    try {
      tariffs = await api(`/api/tg/staff/tariffs?computerId=${encodeURIComponent(pc.id)}`);
    } catch (e) {
      state.error = e.message;
      render();
      return;
    }
    if (!tariffs.length) {
      alert("Нет доступных тарифов для этой зоны");
      return;
    }

    const wrap = document.createElement("div");
    wrap.className = "sheet-backdrop";
    wrap.id = "startWizard";
    const tariffOpts = tariffs
      .map(
        (t) =>
          `<option value="${esc(t.id)}">${esc(t.name)}${
            t.fixedDurationMinutes ? " · " + fmtDuration(t.fixedDurationMinutes) : ""
          }${t.fixedPrice != null ? " · " + money(t.fixedPrice) : ""}</option>`
      )
      .join("");
    wrap.innerHTML = `
      <div class="sheet sheet--tall">
        <h3>Старт · ${esc(pc.name)}</h3>
        <label>Тариф<select id="swTariff">${tariffOpts}</select></label>
        <label>Минуты<input id="swMins" type="number" min="1" value="60" /></label>
        <label>Оплата
          <select id="swPay">
            <option value="Cash">Наличные</option>
            <option value="KaspiQr">Kaspi QR</option>
            <option value="Balance">С баланса клиента</option>
            <option value="Free">Бесплатно</option>
          </select>
        </label>
        <label>Имя гостя<input id="swGuest" placeholder="необязательно" /></label>
        <label>ID клиента<input id="swCust" placeholder="GUID или поиск ниже" /></label>
        <label>Поиск клиента<input id="swSearch" placeholder="телефон / имя" /></label>
        <div id="swHits" class="search-hits"></div>
        <label class="check"><input type="checkbox" id="swWake" ${
          isOfflineFreePc(pc) ? "checked" : ""
        } /> Включить ПК по LAN, если выкл</label>
        <label class="check"><input type="checkbox" id="swBank" /> Списать из банка времени</label>
        <div class="err" id="swErr"></div>
        <button class="btn" id="swGo">Запустить</button>
        <button class="btn ghost" id="swClose">Отмена</button>
      </div>`;
    document.body.appendChild(wrap);
    const close = () => wrap.remove();
    wrap.querySelector("#swClose").onclick = close;
    wrap.addEventListener("click", (e) => {
      if (e.target === wrap) close();
    });

    let searchTimer;
    wrap.querySelector("#swSearch").oninput = (e) => {
      clearTimeout(searchTimer);
      const q = e.target.value.trim();
      searchTimer = setTimeout(async () => {
        const box = wrap.querySelector("#swHits");
        if (q.length < 2) {
          box.innerHTML = "";
          return;
        }
        try {
          const hits = await api(`/api/tg/staff/customers?q=${encodeURIComponent(q)}`);
          box.innerHTML = (hits || [])
            .map(
              (h) =>
                `<button type="button" class="hit" data-id="${esc(h.id)}"><strong>${esc(
                  h.fullName
                )}</strong><span>${esc(h.phone)} · ${money(h.balance)}</span></button>`
            )
            .join("") || `<div class="muted">Никого не нашли</div>`;
          box.querySelectorAll("[data-id]").forEach((b) => {
            b.onclick = () => {
              wrap.querySelector("#swCust").value = b.getAttribute("data-id");
              wrap.querySelector("#swGuest").value = b.querySelector("strong").textContent;
            };
          });
        } catch (err) {
          box.innerHTML = `<div class="err">${esc(err.message)}</div>`;
        }
      }, 280);
    };

    wrap.querySelector("#swTariff").onchange = () => {
      const t = tariffs.find((x) => String(x.id) === wrap.querySelector("#swTariff").value);
      if (t && t.fixedDurationMinutes) wrap.querySelector("#swMins").value = t.fixedDurationMinutes;
    };
    wrap.querySelector("#swTariff").dispatchEvent(new Event("change"));

    wrap.querySelector("#swGo").onclick = async () => {
      const err = wrap.querySelector("#swErr");
      err.textContent = "";
      const cust = (wrap.querySelector("#swCust").value || "").trim();
      try {
        await api("/api/tg/staff/sessions/start", {
          method: "POST",
          body: JSON.stringify({
            computerId: pc.id,
            tariffId: wrap.querySelector("#swTariff").value,
            durationMinutes: Number(wrap.querySelector("#swMins").value) || 60,
            paymentMethod: wrap.querySelector("#swPay").value,
            guestName: wrap.querySelector("#swGuest").value || null,
            customerId: cust || null,
            useTimeBank: wrap.querySelector("#swBank").checked,
            wakeIfOffline: wrap.querySelector("#swWake")?.checked !== false,
          }),
        });
        close();
        state.flash = `Сеанс на ${pc.name} запущен`;
        await loadStaff();
        render();
      } catch (e) {
        err.textContent = e.message;
      }
    };
  }

  async function staffExtendWizard(pc) {
    const wrap = document.createElement("div");
    wrap.className = "sheet-backdrop";
    wrap.id = "extendWizard";
    wrap.innerHTML = `
      <div class="sheet sheet--tall">
        <h3>Продлить · ${esc(pc.name)}</h3>
        <label>Минуты
          <div class="chip-row" id="exPresets">
            <button type="button" class="chip-btn" data-m="15">+15</button>
            <button type="button" class="chip-btn" data-m="30">+30</button>
            <button type="button" class="chip-btn on" data-m="60">+60</button>
            <button type="button" class="chip-btn" data-m="120">+120</button>
          </div>
          <input id="exMins" type="number" min="1" value="60" />
        </label>
        <label>Оплата
          <select id="exPay">
            <option value="Cash">Наличные</option>
            <option value="KaspiQr">Kaspi QR</option>
            <option value="Balance">С баланса клиента</option>
          </select>
        </label>
        <div class="muted" id="exQuote" style="margin-top:10px">Считаем сумму…</div>
        <div class="err" id="exErr"></div>
        <button class="btn" id="exGo">Продлить</button>
        <button class="btn ghost" id="exClose">Отмена</button>
      </div>`;
    document.body.appendChild(wrap);
    const close = () => wrap.remove();
    wrap.querySelector("#exClose").onclick = close;
    wrap.addEventListener("click", (e) => {
      if (e.target === wrap) close();
    });

    const minsEl = wrap.querySelector("#exMins");
    const quoteEl = wrap.querySelector("#exQuote");
    let quoteTimer;

    async function refreshQuote() {
      const mins = Number(minsEl.value) || 0;
      if (mins < 1) {
        quoteEl.textContent = "Укажите минуты";
        return;
      }
      quoteEl.textContent = "Считаем сумму…";
      try {
        const quote = await api(
          `/api/tg/staff/sessions/${pc.sessionId}/extend-quote?minutes=${mins}`
        );
        quoteEl.textContent = quote
          ? `К оплате ≈ ${money(quote.expectedAmount)} · ${quote.tariffName || ""}`
          : "Не удалось получить расчёт";
      } catch (e) {
        quoteEl.textContent = e.message || "Не удалось получить расчёт";
      }
    }

    wrap.querySelectorAll("#exPresets [data-m]").forEach((b) => {
      b.onclick = () => {
        wrap.querySelectorAll("#exPresets [data-m]").forEach((x) => x.classList.remove("on"));
        b.classList.add("on");
        minsEl.value = b.getAttribute("data-m");
        void refreshQuote();
      };
    });
    minsEl.oninput = () => {
      clearTimeout(quoteTimer);
      quoteTimer = setTimeout(() => void refreshQuote(), 250);
    };
    void refreshQuote();

    wrap.querySelector("#exGo").onclick = async () => {
      const err = wrap.querySelector("#exErr");
      err.textContent = "";
      const mins = Number(minsEl.value) || 0;
      if (mins < 1) {
        err.textContent = "Укажите минуты";
        return;
      }
      try {
        await api(`/api/tg/staff/sessions/${pc.sessionId}/extend`, {
          method: "POST",
          body: JSON.stringify({
            additionalMinutes: mins,
            paymentMethod: wrap.querySelector("#exPay").value,
          }),
        });
        close();
        state.flash = `Продлено на ${fmtDuration(mins)}`;
        await loadStaff();
        render();
      } catch (e) {
        err.textContent = e.message;
      }
    };
  }

  async function staffTransferWizard(pc) {
    if (!pc.sessionId) return;
    let targets = [];
    try {
      targets = await api(`/api/tg/staff/sessions/${pc.sessionId}/transfer-targets`);
    } catch (e) {
      alert(e.message);
      return;
    }
    if (!targets.length) {
      alert("Нет свободных ПК в той же зоне");
      return;
    }
    const wrap = document.createElement("div");
    wrap.className = "sheet-backdrop";
    wrap.innerHTML = `
      <div class="sheet sheet--tall">
        <h3>Перенос с ${esc(pc.name)}</h3>
        <p class="muted">Только та же зона. Выкл ПК будут включены автоматически.</p>
        <div class="search-hits" id="trHits">
          ${targets
            .map(
              (t) =>
                `<button type="button" class="hit" data-id="${esc(t.id)}"><strong>${esc(
                  t.name
                )}</strong><span>${esc(t.isOffline ? "Выкл" : "Свободен")}${
                  t.zoneName ? " · " + esc(t.zoneName) : ""
                }</span></button>`
            )
            .join("")}
        </div>
        <button class="btn ghost" id="trClose">Отмена</button>
      </div>`;
    document.body.appendChild(wrap);
    const close = () => wrap.remove();
    wrap.querySelector("#trClose").onclick = close;
    wrap.addEventListener("click", (e) => {
      if (e.target === wrap) close();
    });
    wrap.querySelectorAll("[data-id]").forEach((b) => {
      b.onclick = async () => {
        try {
          await api(`/api/tg/staff/sessions/${pc.sessionId}/transfer`, {
            method: "POST",
            body: JSON.stringify({ targetComputerId: b.getAttribute("data-id") }),
          });
          close();
          state.flash = "Сеанс перенесён";
          await loadStaff();
          render();
        } catch (e) {
          alert(e.message);
        }
      };
    });
  }

  function renderHome(p, bal, bonus) {
    const s = state.home && state.home.activeSession;
    const news = (state.home && state.home.news) || [];
    const hideBal = !!p.comfortHideBalance;
    return `
      <div class="header">
        <div class="brand">SHIFT Club</div>
        <h1>${esc(p.fullName || "Гость")}</h1>
        <div class="sub">${esc(p.loyaltyLevelName || "Участник")} · ${esc(p.phone || "")}</div>
      </div>
      ${state.flash ? `<div class="card ok">${esc(state.flash)}</div>` : ""}
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      <div class="card">
        <div class="row"><span class="muted">Баланс</span><span class="chip">бонусы ${
          hideBal ? "•••" : esc(bonus)
        }</span></div>
        <div class="money">${hideBal ? "•••" : esc(bal)}</div>
      </div>
      ${renderBankBlock(p)}
      ${renderLoyaltyBlock(p)}
      <div class="card" style="border-color:rgba(255,106,0,.35)">
        <div class="row"><strong>SHIFT CASE</strong><span class="chip">на кассе</span></div>
        <div class="muted" style="margin-top:6px">Ключ за новый аккаунт — кейс крутят только у администратора.</div>
        <button class="btn" id="homeOpenCase" style="margin-top:12px">Мои ключи</button>
      </div>
      <div class="card">
        <h2>Сеанс</h2>
        ${
          s
            ? `<div class="row"><strong>${esc(s.computerName || "ПК")}</strong><span class="chip">${fmtDuration(
                s.minutesLeft
              )}</span></div>
               <div class="muted" style="margin-top:6px">${esc(s.tariffName || "")} · ${esc(s.zoneName || "")}</div>
               <button class="btn ghost" id="openSession" style="margin-top:12px">Управление сеансом</button>
               <button class="btn ghost" id="homeOpenBar" style="margin-top:8px">Заказ в бар</button>
               <button class="btn danger" id="endSession" style="margin-top:8px">Завершить сеанс</button>`
            : `<div class="muted">Нет активного сеанса. Центральная кнопка QR — вход на ПК или привязка Telegram.</div>
               <button class="btn" id="homeScanQr" style="margin-top:12px">Сканировать QR ПК</button>`
        }
      </div>
      <div class="card">
        <h2>Новости</h2>
        ${
          news.length
            ? news
                .map(
                  (n) =>
                    `<div class="list-item"><div class="news-title">${esc(n.title)}</div><div class="news-body">${esc(
                      n.body || ""
                    )}</div></div>`
                )
                .join("")
            : `<div class="muted">Пока нет публикаций</div>`
        }
      </div>`;
  }

  function renderCase() {
    const c = state.caseState;
    if (!c) {
      return `
        <div class="header"><div class="brand">SHIFT CASE</div><h1>Кейс</h1></div>
        <div class="card muted">Загрузка…</div>`;
    }
    const catalog = c.catalog || {};
    const keys = Number(c.keysBalance) || 0;
    const prizes = (catalog.prizes || [])
      .slice(0, 12)
      .map(
        (p) =>
          `<div class="list-item"><div class="row"><strong>${esc(p.name)}</strong></div>
           <div class="muted">${esc(p.description || "")}</div></div>`
      )
      .join("");
    return `
      <div class="header">
        <div class="brand">SHIFT CASE</div>
        <h1>${esc(catalog.title || "Кейс")}</h1>
        <div class="sub">Ключей: ${keys}</div>
      </div>
      ${state.flash ? `<div class="card ok">${esc(state.flash)}</div>` : ""}
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      <div class="card">
        <p class="muted">Кейс открывается <strong>только на кассе</strong> — с полной анимацией рулетки.</p>
        <div class="muted" style="margin-top:10px">
          ${
            keys > 0
              ? "Ключ уже на аккаунте — подойди к администратору."
              : "Ключ выдаётся за новый аккаунт. Если создал на ПК — тоже к кассе."
          }
        </div>
      </div>
      <div class="card">
        <h2>Что может выпасть</h2>
        ${prizes || `<div class="muted">Каталог пуст</div>`}
      </div>`;
  }

  function renderLoyalty() {
    const d = state.loyalty;
    const moneyN = (n) => `${Number(n || 0).toFixed(0)} ₸`;
    if (!d) {
      return `
        <div class="header"><div class="brand">SHIFT</div><h1>Лояльность</h1></div>
        <div class="card muted">Загрузка…</div>
        <div class="card"><button class="btn ghost" id="loyaltyBack">Назад</button></div>`;
    }
    const levels = (d.levels || [])
      .map((l) => {
        const min = Number(l.minSpent) || 0;
        const bonus = Number(l.bonusPercent) || 0;
        const disc = Number(l.timeDiscountPercent) || 0;
        const meta =
          min <= 0
            ? "Стартовый уровень"
            : `От ${moneyN(min)}`;
        const perks = [
          bonus > 0 ? `+${bonus}% к пополнению` : null,
          disc > 0 ? `−${disc}% ко времени` : null,
        ]
          .filter(Boolean)
          .join(" · ");
        return `<div class="list-item">
          <div class="row"><strong>${esc(l.name)}</strong><span class="chip">${esc(l.code || "")}</span></div>
          <div class="muted">${esc(meta)}${perks ? " · " + esc(perks) : ""}</div>
        </div>`;
      })
      .join("");

    const streaks = !(d.rewardsEnabled)
      ? `<div class="muted">Награды за серии сейчас выключены.</div>`
      : (d.streakTiers || [])
          .map((t) => {
            const bar = Number(t.barRewards) > 0 ? ` · напиток ×${t.barRewards}` : "";
            return `<div class="list-item">
              <div class="row"><strong>${t.days} дн.</strong><span class="chip">${moneyN(t.bonusAmount)}</span></div>
              <div class="muted">Бонус на счёт${esc(bar)}</div>
            </div>`;
          })
          .join("") || `<div class="muted">Пороги не заданы</div>`;

    const bday = !d.rewardsEnabled
      ? `<div class="muted">Подарок на ДР выключен.</div>`
      : `<div class="money" style="font-size:1.2rem">${moneyN(d.birthdayBonusAmount)} + ${
          d.birthdayTimeBankMinutes || 0
        } мин</div>
         <div class="muted" style="margin-top:6px">Один раз в год. Укажите дату рождения в профиле.</div>`;

    const deposit =
      d.depositBonusEnabled === false
        ? `<div class="muted">Бонус за пополнение выключен.</div>`
        : (d.depositBonusTiers || [])
            .map(
              (t) => `<div class="list-item">
              <div class="row"><strong>от ${moneyN(t.minAmount)}</strong><span class="chip">+${moneyN(t.bonusAmount)}</span></div>
              <div class="muted">На бонусный счёт</div>
            </div>`,
            )
            .join("") || `<div class="muted">Пороги не заданы</div>`;

    return `
      <div class="header"><div class="brand">SHIFT</div><h1>Лояльность</h1>
        <div class="sub">Актуальные условия клуба</div></div>
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      <div class="card"><h2>Уровни</h2>${levels || `<div class="muted">Нет уровней</div>`}</div>
      <div class="card"><h2>Бонус за пополнение</h2>${deposit}</div>
      <div class="card"><h2>Серия визитов</h2>${streaks}</div>
      <div class="card"><h2>День рождения</h2>${bday}</div>
      <div class="card">
        <button class="btn ghost" id="loyaltyBack">Назад</button>
        <button class="btn ghost" id="loyaltyRefresh">Обновить</button>
      </div>`;
  }

  function renderSession() {
    const s = state.home && state.home.activeSession;
    return `
      <div class="header"><div class="brand">SHIFT</div><h1>Сеанс</h1></div>
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      ${state.flash ? `<div class="card ok">${esc(state.flash)}</div>` : ""}
      <div class="card">
        ${
          s
            ? `<div class="money">${fmtDuration(s.minutesLeft)}</div>
               <div class="row" style="margin-top:10px"><span class="muted">ПК</span><strong>${esc(
                 s.computerName || "—"
               )}</strong></div>
               <div class="row"><span class="muted">Зона</span><span>${esc(s.zoneName || "—")}</span></div>
               <div class="row"><span class="muted">Тариф</span><span>${esc(s.tariffName || "—")}</span></div>
               <button class="btn danger" id="endSession" style="margin-top:16px">Завершить сеанс</button>
               <button class="btn ghost" id="sessionBack" style="margin-top:8px">На главную</button>`
            : `<div class="muted">Нет активного сеанса.</div>
               <button class="btn" id="scanPc" style="margin-top:14px">Сканировать QR ПК</button>
               <button class="btn ghost" id="sessionBack" style="margin-top:8px">На главную</button>`
        }
      </div>`;
  }

  function renderBar() {
    const items = state.catalog || [];
    const cartCount = Object.values(state.cart).reduce((a, b) => a + b, 0);
    const session = state.home && state.home.activeSession;
    const canOrder = !!session;
    return `
      <div class="header"><div class="brand">SHIFT</div><h1>Бар</h1></div>
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      ${state.flash ? `<div class="card ok">${esc(state.flash)}</div>` : ""}
      ${
        canOrder
          ? `<div class="card ok" style="margin-bottom:0">
              Сеанс на <strong>${esc(session.computerName || "ПК")}</strong>
              ${session.minutesLeft != null ? ` · ${fmtDuration(session.minutesLeft)}` : ""}
            </div>`
          : `<div class="card err">
              Заказ из бара доступен только во время <strong>активного сеанса на ПК</strong> клуба.
              Сначала войдите на компьютер — по паролю или QR.
              <button class="btn ghost" id="goSession" style="margin-top:12px">К сеансу</button>
            </div>`
      }
      <div class="card">
        ${
          !canOrder
            ? `<div class="muted">Каталог откроется, когда будет активный сеанс на ПК.</div>`
            : items.length
              ? items
                  .map((p) => {
                    const q = state.cart[p.id] || 0;
                    return `<div class="product">
                    ${
                      p.imageUrl
                        ? `<img src="${esc(p.imageUrl)}" alt="" />`
                        : `<div style="width:52px;height:52px;border-radius:10px;background:#222"></div>`
                    }
                    <div class="meta"><div class="name">${esc(p.name)}</div><div class="muted">${money(
                      p.salePrice
                    )}</div></div>
                    <div class="qty">
                      <button data-dec="${p.id}">−</button>
                      <span>${q}</span>
                      <button data-inc="${p.id}">+</button>
                    </div>
                  </div>`;
                  })
                  .join("")
              : `<div class="muted">Каталог пуст</div>`
        }
      </div>
      <div class="card">
        <button class="btn" id="placeOrder" ${canOrder && cartCount ? "" : "disabled"}>
          ${canOrder ? `Оформить · ${cartCount} позиций` : "Нужен сеанс на ПК"}
        </button>
        <button class="btn ghost" id="refreshOrders">Мои заказы</button>
        <div id="ordersBox"></div>
      </div>`;
  }

  function renderStaffFloor(p, s) {
    // Подмешиваем имена гостей / брони из staff home в карту
    const guests = {};
    ((s && s.floor && s.floor.pcs) || []).forEach((pc) => {
      if (pc.guestName) guests[pc.id] = pc.guestName;
      if (state.floorMap && state.floorMap.pcs) {
        const hit = state.floorMap.pcs.find((x) => x.id === pc.id);
        if (!hit) return;
        if (pc.remainingSeconds != null && hit.remainingMinutes == null)
          hit.remainingMinutes = Math.ceil(pc.remainingSeconds / 60);
        if (pc.occupancyDetail) hit.occupancyDetail = pc.occupancyDetail;
        if (pc.occupancy) hit.occupancy = pc.occupancy;
        if (pc.guestName) hit.guestName = pc.guestName;
        if (pc.sessionId) hit.sessionId = pc.sessionId;
        if (pc.stationKind) hit.stationKind = pc.stationKind;
        if (pc.bookingId) {
          hit.bookingId = pc.bookingId;
          hit.bookingContact = pc.bookingContact;
          hit.bookingStartsAt = pc.bookingStartsAt;
          hit.bookingEndsAt = pc.bookingEndsAt;
        }
      }
    });
    if (state.floorMap && state.floorMap.pcs) {
      state.floorMap.pcs.forEach((pc) => {
        if (guests[pc.id]) pc.guestName = guests[pc.id];
      });
    }
    return (
      renderFloorMap(true) +
      `<div class="card muted" style="font-size:.88rem">
        ПК: старт · продление · пауза · перенос · WOL · бронь. Выкл ≠ занят — можно садить.
      </div>`
    );
  }

  function bookingStatusRu(st) {
    const v = String(st || "").toLowerCase();
    if (v === "pending") return "Ожидает";
    if (v === "confirmed") return "Подтверждена";
    if (v === "arrived") return "Пришёл";
    if (v === "active") return "Сеанс";
    return st || "—";
  }

  function renderStaffBookings(s) {
    const list = (s && s.bookings) || [];
    return `
      <div class="header"><div class="brand">SHIFT · Сотрудник</div><h1>Брони</h1>
        <div class="sub">Сегодня и завтра · открытые</div></div>
      ${state.flash ? `<div class="card ok">${esc(state.flash)}</div>` : ""}
      ${state.error ? `<div class="card err">${esc(state.error)}</div>` : ""}
      <div class="card">
        ${
          list.length
            ? list
                .map((b) => {
                  const when = `${fmtClockIso(b.startsAt)}${b.endsAt ? "–" + fmtClockIso(b.endsAt) : ""}`;
                  const pcs = (b.computerNames || []).join(", ") || "—";
                  return `<div class="list-item">
              <div class="row"><strong>${esc(b.contactName || "Гость")}</strong><span class="chip">${esc(
                    bookingStatusRu(b.status)
                  )}</span></div>
              <div class="muted">${esc(when)} · ${esc(pcs)}</div>
              <div class="muted">${esc(b.contactPhone || "")}${b.number ? " · №" + esc(b.number) : ""}</div>
              <div class="row" style="margin-top:8px;gap:8px">
                <button type="button" class="btn ghost" style="margin:0" data-b-arrived="${esc(b.id)}">Пришёл</button>
                <button type="button" class="btn ghost" style="margin:0" data-b-cancel="${esc(b.id)}">Отмена</button>
              </div>
            </div>`;
                })
                .join("")
            : `<div class="muted">Открытых броней нет</div>`
        }
        <button class="btn ghost" id="staffRefresh" style="margin-top:12px">Обновить</button>
      </div>`;
  }

  function renderStaffBar(s) {
    const orders = (s && s.openBarOrders) || [];
    return `
      <div class="header"><div class="brand">SHIFT · Сотрудник</div><h1>Открытые заказы</h1></div>
      <div class="card">
        ${
          orders.length
            ? orders
                .map(
                  (o) => `<div class="list-item">
              <div class="row"><strong>${esc(barOrderStatus(o.status))}</strong><span>${money(o.total)}</span></div>
              <div class="muted">${esc(o.computerName || "—")} · ${esc((o.items || []).join(", "))}</div>
            </div>`
                )
                .join("")
            : `<div class="muted">Открытых заказов нет</div>`
        }
        <button class="btn ghost" id="staffRefresh" style="margin-top:12px">Обновить</button>
      </div>`;
  }

  function renderStaffShift(s) {
    const sh = s && s.shift;
    return `
      <div class="header"><div class="brand">SHIFT · Сотрудник</div><h1>Смена</h1></div>
      <div class="card">
        ${
          sh
            ? `<div class="row"><span class="muted">Касса</span><strong>${esc(sh.cashRegisterName)}</strong></div>
               <div class="row"><span class="muted">Открыта</span><span>${esc(
                 new Date(sh.openedAt).toLocaleString()
               )}</span></div>
               <div class="grid2" style="margin-top:12px">
                 <div class="stat"><div class="l">Наличные</div><div class="v">${money(sh.cashSales)}</div></div>
                 <div class="stat"><div class="l">Kaspi</div><div class="v">${money(
                   sh.kaspiSales != null ? sh.kaspiSales : sh.cardSales
                 )}</div></div>
               </div>`
            : `<div class="muted">Нет открытой кассовой смены для вашего сотрудника. Откройте смену в панели управления или кнопкой «Смена» в боте.</div>`
        }
        <button class="btn ghost" id="staffRefresh" style="margin-top:12px">Обновить</button>
      </div>`;
  }

  function renderAccount(p) {
    const canEdit = hasCustomer(p);
    return `
      <div class="header"><div class="brand">SHIFT</div><h1>Профиль</h1>
        <div class="sub">${
          canEdit
            ? `Telegram · ID ${esc(p.telegramUserId || "")}`
            : "Аккаунт клуба ещё не создан"
        }</div></div>
      ${canEdit ? renderBankBlock(p) + renderLoyaltyBlock(p) : ""}
      <div class="card">
        <h2>Данные</h2>
        ${
          canEdit
            ? `<label>Имя<input id="pfFirst" value="${esc(p.firstName || "")}" /></label>
               <label>Фамилия<input id="pfLast" value="${esc(p.lastName || "")}" /></label>
               <label>Телефон<input id="pfPhone" value="${esc(p.phone || "")}" inputmode="tel" /></label>
               <label>ИИН<input id="pfIin" value="${esc(p.iin || "")}" inputmode="numeric" maxlength="12" /></label>
               <label>Новый пароль<input id="pfPass" type="password" placeholder="оставьте пустым чтобы не менять" /></label>
               <div class="err" id="pfErr"></div>
               <div class="ok" id="pfOk"></div>
               <button class="btn" id="saveProfile" style="margin-top:14px">Сохранить</button>`
            : `<div class="muted" style="line-height:1.45">Создайте аккаунт клуба или привяжите существующий (телефон + пароль с ПК).</div>
               <button class="btn" id="goRegister" style="margin-top:12px">Создать или привязать аккаунт</button>
               <button class="btn ghost" id="profileScanQr">Сканировать QR на ПК</button>`
        }
      </div>
      ${
        canEdit
          ? `<div class="card">
        <h2>Настройки на ПК</h2>
        <div class="toggle"><span>Скрыть баланс</span>
          <input type="checkbox" id="hideBal" ${p.comfortHideBalance ? "checked" : ""} /></div>
        <label>Язык уведомлений
          <select id="lang">
            <option value="ru" ${p.comfortLanguage === "ru" ? "selected" : ""}>Русский</option>
            <option value="kk" ${p.comfortLanguage === "kk" ? "selected" : ""}>Қазақша</option>
            <option value="en" ${p.comfortLanguage === "en" ? "selected" : ""}>English</option>
          </select>
        </label>
        <p class="muted" style="margin:8px 0 0;font-size:.8rem">Звук и яркость на ПК из Mini App не меняются.</p>
        <button class="btn ghost" id="saveComfort" style="margin-top:12px">Сохранить настройки</button>
        <div class="ok" id="comfortOk"></div>
      </div>`
          : ""
      }`;
  }

  function bindCommon() {
    el.querySelectorAll("[data-panel]").forEach((b) => {
      b.onclick = async () => {
        const panel = b.getAttribute("data-panel");
        setPanel(panel);
        try {
          if (panel === "staff") await loadStaff();
          else {
            if (state.auth && needsClubAccount(state.auth)) {
              renderWelcome(readInitData());
              return;
            }
            await loadHome();
          }
          render();
        } catch (e) {
          if (panel === "client" && /клиентск|аккаунт/i.test(e.message || "")) {
            renderWelcome(readInitData());
            return;
          }
          state.error = e.message;
          render();
        }
      };
    });

    el.querySelectorAll("[data-tab]").forEach((b) => {
      b.onclick = async () => {
        state.tab = b.getAttribute("data-tab");
        state.flash = "";
        state.error = "";
        try {
          if (state.panel === "client" && state.tab === "map") await loadFloorMap();
          if (state.panel === "client" && state.tab === "loyalty") await loadLoyalty();
          if (state.panel === "client" && state.tab === "case") await loadCase();
          if (state.panel === "client" && state.tab === "session") await loadHome();
          if (state.panel === "client" && state.tab === "bar") {
            await loadHome();
            if (state.home && state.home.activeSession) await loadBar();
            else {
              state.catalog = [];
              state.cart = {};
            }
          }
        } catch (e) {
          state.error = e.message;
        }
        render();
      };
    });

    const navQr = document.getElementById("navQr");
    if (navQr) navQr.onclick = () => startQrScan();

    const goReg = document.getElementById("goRegister");
    if (goReg) goReg.onclick = () => renderRegister(readInitData());

    const profileScan = document.getElementById("profileScanQr");
    if (profileScan) profileScan.onclick = () => startQrScan({ stayOpen: true });

    const saveProfile = document.getElementById("saveProfile");
    if (saveProfile) {
      saveProfile.onclick = async () => {
        const err = document.getElementById("pfErr");
        const ok = document.getElementById("pfOk");
        err.textContent = "";
        ok.textContent = "";
        try {
          const auth = await api("/api/tg/profile", {
            method: "PUT",
            body: JSON.stringify({
              firstName: document.getElementById("pfFirst").value,
              lastName: document.getElementById("pfLast").value,
              phone: document.getElementById("pfPhone").value,
              iin: document.getElementById("pfIin").value,
              newPassword: document.getElementById("pfPass").value || null,
            }),
          });
          state.auth = auth;
          localStorage.setItem(TOKEN_KEY, auth.accessToken);
          ok.textContent = "Сохранено";
          if (state.panel === "staff") await loadStaff();
          else await loadHome();
        } catch (e) {
          err.textContent = e.message;
        }
      };
    }
  }

  async function confirmQrPayload(raw, opts = {}) {
    const payload = String(raw || "").trim();
    if (!payload) throw new Error("Пустой QR или код");

    const result = await api("/api/tg/qr/confirm", {
      method: "POST",
      body: JSON.stringify({
        initData: opts.initData || readInitData() || null,
        qrPayload: payload,
      }),
    });

    const auth = result.auth;
    state.auth = auth;
    if (auth && auth.accessToken) localStorage.setItem(TOKEN_KEY, auth.accessToken);
    state.flash = result.message || "Готово";
    state.error = "";
    state.pendingQrTicket = result.needsRegistration
      ? { code: result.ticketCode, id: result.ticketId, purpose: result.purpose, message: result.message }
      : null;

    if (result.needsRegistration) {
      if (!result.ticketCode) {
        state.error = "QR подтверждён, но код потерян. Отсканируйте снова.";
        renderWelcome(opts.initData || readInitData());
        return result;
      }
      renderQrRegister(opts.initData || readInitData(), state.pendingQrTicket);
      return result;
    }

    if (needsClubAccount(auth)) {
      state.flash = result.message || "Создайте или привяжите аккаунт клуба.";
      renderWelcome(opts.initData || readInitData());
      return result;
    }

    state.flash = (result.message || "Готово") + " Вернитесь к ПК — вход уже идёт.";
    setPanel("client");
    try {
      await loadHome();
    } catch (_) {
      /* home optional right after link */
    }
    state.tab = "home";
    render();
    if (typeof tg !== "undefined" && tg && typeof tg.HapticFeedback?.notificationOccurred === "function") {
      try {
        tg.HapticFeedback.notificationOccurred("success");
      } catch (_) {}
    }
    return result;
  }

  function renderQrRegister(initData, ticket) {
    const name = (state.auth && state.auth.fullName) || "";
    const parts = String(name).trim().split(/\s+/);
    const first = parts[0] || "";
    const last = parts.length > 1 ? parts.slice(1).join(" ") : "";
    const msg = (ticket && ticket.message) || state.flash || "Telegram подтверждён.";
    const code = (ticket && ticket.code) || "";
    if (!code) {
      el.innerHTML = `
        <div class="header"><div class="brand">SHIFT Club</div><h1>Нужен QR</h1></div>
        <div class="card">
          <p class="muted">Отсканируйте QR на экране ПК ещё раз.</p>
          <button class="btn" id="qrRegRescan">Сканировать QR</button>
          <button class="btn ghost" id="qrRegBack">Назад</button>
        </div>`;
      document.getElementById("qrRegRescan").onclick = () => startQrScan({ stayOpen: true });
      document.getElementById("qrRegBack").onclick = () => {
        if (needsClubAccount(state.auth)) renderWelcome(initData || readInitData());
        else {
          state.tab = "home";
          render();
        }
      };
      return;
    }

    el.innerHTML = `
      <div class="header">
        <div class="brand">SHIFT Club</div>
        <h1>Аккаунт для ПК</h1>
        <div class="sub">${esc(msg)}</div>
      </div>
      <div class="card welcome-card">
        <p class="muted" style="margin:0 0 12px;line-height:1.45">
          После сохранения вы автоматически войдёте на тот ПК, где открыт QR.
          Можно заполнить данные здесь или на экране компьютера.
        </p>
        <div class="chip" style="margin-bottom:12px">Код ${esc(code)}</div>
        <label>Имя<input id="qrFirst" value="${esc(first)}" autocomplete="given-name" /></label>
        <label>Фамилия<input id="qrLast" value="${esc(last)}" autocomplete="family-name" /></label>
        <label>Телефон<input id="qrPhone" inputmode="tel" placeholder="77001234567" autocomplete="tel" /></label>
        <label>Пароль (мин. 4)<input id="qrPass" type="password" autocomplete="new-password" /></label>
        <label class="check"><input type="checkbox" id="qrLink" /> Уже есть аккаунт — привязать этот Telegram</label>
        <div class="err" id="qrRegErr"></div>
        <div class="ok" id="qrRegOk"></div>
        <button class="btn" id="qrRegSubmit">Создать и войти на ПК</button>
        <button class="btn ghost" id="qrRegPc">Заполню на ПК</button>
        <button class="btn ghost" id="qrRegBack">Назад</button>
      </div>`;

    const syncCta = () => {
      document.getElementById("qrRegSubmit").textContent =
        document.getElementById("qrLink").checked ? "Привязать и войти на ПК" : "Создать и войти на ПК";
    };
    document.getElementById("qrLink").onchange = syncCta;

    document.getElementById("qrRegBack").onclick = () => {
      if (needsClubAccount(state.auth)) renderWelcome(initData || readInitData());
      else {
        state.tab = "home";
        render();
      }
    };

    document.getElementById("qrRegPc").onclick = () => {
      document.getElementById("qrRegOk").textContent =
        "Вернитесь к ПК — справа откроется форма. После сохранения вход будет автоматическим.";
      document.getElementById("qrRegErr").textContent = "";
    };

    document.getElementById("qrRegSubmit").onclick = async () => {
      const err = document.getElementById("qrRegErr");
      const ok = document.getElementById("qrRegOk");
      const btn = document.getElementById("qrRegSubmit");
      err.textContent = "";
      ok.textContent = "";
      const phone = (document.getElementById("qrPhone").value || "").replace(/\D/g, "");
      if (phone.length < 10) {
        err.textContent = "Укажите телефон — не менее 10 цифр.";
        return;
      }
      if ((document.getElementById("qrPass").value || "").trim().length < 4) {
        err.textContent = "Пароль: минимум 4 символа.";
        return;
      }
      btn.disabled = true;
      try {
        const result = await api("/api/tg/qr/register", {
          method: "POST",
          body: JSON.stringify({
            initData: initData || readInitData(),
            ticketCode: ticket.code,
            firstName: document.getElementById("qrFirst").value,
            lastName: document.getElementById("qrLast").value,
            phone: document.getElementById("qrPhone").value,
            password: document.getElementById("qrPass").value,
            linkExisting: document.getElementById("qrLink").checked,
          }),
        });
        state.auth = result.auth;
        state.pendingQrTicket = null;
        if (result.auth && result.auth.accessToken) localStorage.setItem(TOKEN_KEY, result.auth.accessToken);
        ok.textContent = result.message || "Готово. Вернитесь к ПК — вход уже идёт.";
        state.flash = ok.textContent;
        if (typeof tg !== "undefined" && tg && typeof tg.HapticFeedback?.notificationOccurred === "function") {
          try {
            tg.HapticFeedback.notificationOccurred("success");
          } catch (_) {}
        }
        setTimeout(async () => {
          setPanel("client");
          try {
            await loadHome();
          } catch (_) {}
          state.tab = "home";
          render();
        }, 900);
      } catch (e) {
        err.textContent = e.message || "Ошибка регистрации";
        btn.disabled = false;
      }
    };
  }

  function startQrScan(opts = {}) {
    if (!tg || typeof tg.showScanQrPopup !== "function") {
      state.error = "Сканер QR недоступен в этом клиенте Telegram. Введите код с экрана вручную.";
      if (needsClubAccount(state.auth)) {
        renderWelcome(readInitData());
        const err = document.getElementById("welcomeErr");
        if (err) err.textContent = state.error;
        return;
      }
      state.tab = "home";
      render();
      return;
    }

    tg.showScanQrPopup({ text: "QR на экране ПК — вход или привязка Telegram" }, (text) => {
      if (!text) {
        state.flash = "Сканирование отменено";
        if (!needsClubAccount(state.auth)) {
          state.tab = "home";
          render();
        }
        return;
      }
      try {
        tg.closeScanQrPopup();
      } catch (_) {}

      confirmQrPayload(text, { initData: readInitData() }).catch((e) => {
        state.error = e.message || "Не удалось подтвердить QR";
        if (needsClubAccount(state.auth)) {
          renderWelcome(readInitData());
          const err = document.getElementById("welcomeErr");
          if (err) err.textContent = state.error;
        } else {
          state.tab = "home";
          render();
        }
      });
    });
  }

  async function endActiveSession() {
    const s = state.home && state.home.activeSession;
    if (!s) return;
    const choice = await showThreeChoice(
      "Завершить сеанс?",
      "Сеанс на ПК закроется, вы останетесь в аккаунте.\nСохранить остаток минут в банк этой зоны?",
      "Сохранить минуты",
      "Без сохранения"
    );
    if (choice === "cancel") return;
    const save = choice === "save";
    state.error = "";
    state.flash = "";
    try {
      await api("/api/tg/session/end", {
        method: "POST",
        body: JSON.stringify({ saveRemainingToTimeBank: save }),
      });
      state.cart = {};
      state.flash = save
        ? "Сеанс завершён. Остаток сохранён в банк зоны."
        : "Сеанс завершён без сохранения минут.";
      await loadHome();
      state.tab = "home";
      render();
    } catch (e) {
      state.error = e.message;
      render();
    }
  }

  async function loadBar() {
    try {
      const cat = await api("/api/tg/bar/catalog");
      state.catalog = cat.products || [];
      state.error = "";
    } catch (e) {
      state.error = e.message;
      state.catalog = [];
    }
    render();
  }

  function bindClientActions() {
    const goSession = document.getElementById("goSession");
    if (goSession) {
      goSession.onclick = () => {
        state.tab = "session";
        render();
      };
    }

    const homeScan = document.getElementById("homeScanQr");
    if (homeScan) homeScan.onclick = () => startQrScan({ stayOpen: true });

    const homeOpenCase = document.getElementById("homeOpenCase");
    if (homeOpenCase) {
      homeOpenCase.onclick = async () => {
        state.tab = "case";
        state.error = "";
        try {
          await loadCase();
        } catch (e) {
          state.error = e.message;
        }
        render();
      };
    }

    const homeOpenBar = document.getElementById("homeOpenBar");
    if (homeOpenBar) {
      homeOpenBar.onclick = async () => {
        state.tab = "bar";
        try {
          await loadHome();
          if (state.home && state.home.activeSession) await loadBar();
        } catch (e) {
          state.error = e.message;
        }
        render();
      };
    }

    const caseOpenBtn = document.getElementById("caseOpenBtn");
    if (caseOpenBtn) {
      caseOpenBtn.onclick = () => {
        state.flash = "Кейс открывается только на кассе у администратора.";
        render();
      };
    }

    const openSession = document.getElementById("openSession");
    if (openSession) {
      openSession.onclick = () => {
        state.tab = "session";
        state.error = "";
        render();
      };
    }

    const sessionBack = document.getElementById("sessionBack");
    if (sessionBack) {
      sessionBack.onclick = () => {
        state.tab = "home";
        state.error = "";
        render();
      };
    }

    const endSession = document.getElementById("endSession");
    if (endSession) endSession.onclick = () => void endActiveSession();

    const openLoyalty = document.getElementById("openLoyalty");
    if (openLoyalty) {
      openLoyalty.onclick = async () => {
        state.tab = "loyalty";
        try {
          await loadLoyalty();
        } catch (e) {
          state.error = e.message;
        }
        render();
      };
    }

    const loyaltyBack = document.getElementById("loyaltyBack");
    if (loyaltyBack) {
      loyaltyBack.onclick = () => {
        state.tab = "home";
        state.error = "";
        render();
      };
    }

    const loyaltyRefresh = document.getElementById("loyaltyRefresh");
    if (loyaltyRefresh) {
      loyaltyRefresh.onclick = async () => {
        try {
          state.error = "";
          await loadLoyalty();
          render();
        } catch (e) {
          state.error = e.message;
          render();
        }
      };
    }

    const scan = document.getElementById("scanPc");
    if (scan) scan.onclick = () => startQrScan();

    el.querySelectorAll("[data-inc]").forEach((b) => {
      b.onclick = () => {
        if (!(state.home && state.home.activeSession)) return;
        const id = b.getAttribute("data-inc");
        state.cart[id] = (state.cart[id] || 0) + 1;
        render();
      };
    });
    el.querySelectorAll("[data-dec]").forEach((b) => {
      b.onclick = () => {
        if (!(state.home && state.home.activeSession)) return;
        const id = b.getAttribute("data-dec");
        state.cart[id] = Math.max(0, (state.cart[id] || 0) - 1);
        if (!state.cart[id]) delete state.cart[id];
        render();
      };
    });

    const place = document.getElementById("placeOrder");
    if (place) {
      place.onclick = async () => {
        state.error = "";
        state.flash = "";
        try {
          await loadHome();
          if (!(state.home && state.home.activeSession)) {
            throw new Error("Нужен активный сеанс на ПК клуба");
          }
          const items = Object.entries(state.cart).map(([productId, quantity]) => ({
            productId,
            quantity,
          }));
          if (!items.length) throw new Error("Корзина пуста");
          const order = await api("/api/tg/bar/orders", {
            method: "POST",
            body: JSON.stringify({ items, paymentMode: "PayAtCashier" }),
          });
          state.cart = {};
          state.flash = `Заказ принят · ${money(order.total)} · оплата у кассы`;
          render();
        } catch (e) {
          state.error = e.message;
          render();
        }
      };
    }

    const refreshOrders = document.getElementById("refreshOrders");
    if (refreshOrders) {
      refreshOrders.onclick = async () => {
        try {
          const orders = await api("/api/tg/bar/orders");
          const box = document.getElementById("ordersBox");
          if (box) {
            box.innerHTML =
              (orders || [])
                .slice(0, 8)
                .map(
                  (o) =>
                    `<div class="list-item"><div class="row"><strong>${esc(
                      barOrderStatus(o.status)
                    )}</strong><span>${money(o.total)}</span></div>
                   <div class="muted">${esc((o.items || []).join(", "))}</div></div>`
                )
                .join("") || `<div class="muted" style="margin-top:10px">Заказов нет</div>`;
          }
        } catch (e) {
          state.error = e.message;
          render();
        }
      };
    }

    const saveComfort = document.getElementById("saveComfort");
    if (saveComfort) {
      saveComfort.onclick = async () => {
        try {
          const prev = state.auth || {};
          const auth = await api("/api/tg/comfort", {
            method: "PUT",
            body: JSON.stringify({
              comfortHideBalance: document.getElementById("hideBal").checked,
              comfortSoundEnabled: prev.comfortSoundEnabled !== false,
              comfortLanguage: document.getElementById("lang").value,
              comfortBrightness:
                prev.comfortBrightness != null ? Number(prev.comfortBrightness) : 100,
            }),
          });
          state.auth = auth;
          if (auth && auth.accessToken) localStorage.setItem(TOKEN_KEY, auth.accessToken);
          document.getElementById("comfortOk").textContent = "Сохранено";
          document.getElementById("comfortOk").className = "ok";
        } catch (e) {
          document.getElementById("comfortOk").textContent = e.message;
          document.getElementById("comfortOk").className = "err";
        }
      };
    }
  }

  bootstrap();
})();
