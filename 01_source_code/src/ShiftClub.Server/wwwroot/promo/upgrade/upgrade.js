(() => {
  const money = (n) => `${Math.round(Number(n) || 0).toLocaleString("ru-RU")} ₸`;
  const CHANNEL = "shift.deskDisplay";
  const LS_KEY = "shift.deskDisplay.cmd";

  const el = {
    priceBoard: document.getElementById("priceBoard"),
    promoChip: document.getElementById("promoChip"),
    endsLabel: document.getElementById("endsLabel"),
    qr: document.getElementById("qr"),
    qrHint: document.getElementById("qrHint"),
    tgLink: document.getElementById("tgLink"),
    footNote: document.getElementById("footNote"),
    promoBoard: document.getElementById("promoBoard"),
    caseFrame: document.getElementById("caseFrame"),
  };

  let lastCommandId = null;
  let caseOpen = false;

  function esc(s) {
    return String(s || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  async function api(path, opts = {}) {
    const headers = { Accept: "application/json" };
    if (opts.body) headers["Content-Type"] = "application/json";
    const res = await fetch(path, { ...opts, headers, cache: "no-store" });
    const json = await res.json().catch(() => ({}));
    if (!res.ok || json.success === false)
      throw new Error(json.message || `HTTP ${res.status}`);
    return json.data;
  }

  function renderPrices(board) {
    const promo = board && board.promo;
    if (promo && promo.active) {
      const label = promo.title || promo.label || "−30% пока обновляем";
      el.promoChip.textContent = label;
      if (promo.endsAt) {
        const d = new Date(promo.endsAt);
        const dd = String(d.getDate()).padStart(2, "0");
        const mm = String(d.getMonth() + 1).padStart(2, "0");
        el.endsLabel.textContent = `до ${dd}.${mm}`;
      }
      el.footNote.textContent = `${label} · пакеты выгоднее · аккаунт в Telegram · кейс на кассе`;
    }

    const zones = (board && board.zones) || [];
    if (!zones.length) {
      el.priceBoard.innerHTML = `<p class="muted">Прайс временно недоступен</p>`;
      return;
    }

    el.priceBoard.innerHTML = zones
      .map((z) => {
        const rows = (z.rows || [])
          .map((r) => {
            const hasPromo =
              r.originalPrice != null && Number(r.originalPrice) > Number(r.price);
            return `<div class="urow${hasPromo ? " urow--promo" : ""}">
              <span class="urow__label">${esc(r.label)}</span>
              <span class="urow__price">${
                hasPromo ? `<s>${esc(money(r.originalPrice))}</s>` : ""
              }${esc(money(r.price))}</span>
            </div>`;
          })
          .join("");
        return `<article class="uzone" style="--z:${esc(z.color || "#ff6a00")}">
          <h3 class="uzone__name">${esc(z.name)}</h3>
          ${rows}
        </article>`;
      })
      .join("");
  }

  function showCase(customerId, commandId, displayToken) {
    if (!customerId || !el.caseFrame) return;
    const id = String(customerId);
    const cmd = commandId ? String(commandId) : "";
    const token = displayToken ? String(displayToken) : "";
    if (caseOpen && lastCommandId && cmd && lastCommandId === cmd) return;
    if (!token) {
      console.warn("desk-display: нет displayToken — экран без логина кассы не откроет кейс");
    }

    lastCommandId = cmd || id;
    caseOpen = true;
    document.body.classList.add("upgrade-case-on");
    el.caseFrame.hidden = false;
    const q = new URLSearchParams({
      desk: "1",
      embed: "1",
      from: "upgrade",
      autostart: "1",
      customerId: id,
      t: String(Date.now()),
    });
    if (cmd) q.set("cmd", cmd);
    if (token) q.set("token", token);
    el.caseFrame.src = `/site/case.html?${q.toString()}`;
  }

  function hideCase() {
    caseOpen = false;
    document.body.classList.remove("upgrade-case-on");
    if (el.caseFrame) {
      el.caseFrame.hidden = true;
      el.caseFrame.src = "about:blank";
    }
  }

  function handleCommand(raw) {
    if (!raw) return;
    let data = raw;
    if (typeof raw === "string") {
      try {
        data = JSON.parse(raw);
      } catch {
        return;
      }
    }
    const customerId = data.customerId || data.CustomerId;
    const commandId = data.commandId || data.CommandId || null;
    const displayToken = data.displayToken || data.DisplayToken || null;
    if (!customerId) return;
    showCase(customerId, commandId, displayToken);
  }

  function listenLocal() {
    try {
      const bc = new BroadcastChannel(CHANNEL);
      bc.onmessage = (ev) => handleCommand(ev.data);
    } catch {
      /* old browser */
    }
    window.addEventListener("storage", (ev) => {
      if (ev.key === LS_KEY && ev.newValue) handleCommand(ev.newValue);
    });
  }

  async function pollServer() {
    try {
      const cmd = await api(`/api/public/desk-display?_=${Date.now()}`);
      if (cmd && (cmd.commandId || cmd.CommandId)) {
        const id = String(cmd.commandId || cmd.CommandId);
        if (id !== lastCommandId) handleCommand(cmd);
      }
    } catch {
      /* ignore */
    }
  }

  async function bootstrap() {
    try {
      const [board, club] = await Promise.all([
        api("/api/public/price"),
        typeof loadClub === "function" ? loadClub() : Promise.resolve(null),
      ]);
      renderPrices(board);

      const bot =
        typeof botLink === "function"
          ? botLink(club && (club.telegramBotUsername || club.botUsername), "upgrade")
          : {
              username: "shift_panel_bot",
              botUrl: "https://t.me/shift_panel_bot",
              miniAppUrl: "https://t.me/shift_panel_bot?startapp=upgrade",
            };

      const mini = bot.miniAppUrl || `${bot.botUrl}?startapp=upgrade`;
      if (typeof setQr === "function") {
        setQr(el.qr, mini, el.qrHint, `@${bot.username}`, 16);
      } else if (el.qr) {
        el.qr.src = `/api/public/qr.png?d=${encodeURIComponent(mini)}&s=16`;
      }
      if (el.qrHint) el.qrHint.textContent = `@${bot.username}`;
      if (el.tgLink) {
        el.tgLink.href = mini;
        el.tgLink.textContent = "Создать аккаунт";
      }
    } catch {
      el.priceBoard.innerHTML = `<p class="muted">Не удалось загрузить прайс</p>`;
    }

    listenLocal();
    window.addEventListener("message", (ev) => {
      const d = ev.data;
      if (!d || d.type !== "shift.case.done") return;
      // Кейс сам держит приз 10 с — закрываем оверлей сразу по сообщению
      hideCase();
    });
    void pollServer();
    setInterval(() => void pollServer(), 1200);
  }

  bootstrap();
})();
