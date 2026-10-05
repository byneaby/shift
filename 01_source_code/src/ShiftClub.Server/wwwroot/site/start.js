(() => {
  const DEFAULT_BOT = "shift_panel_bot";
  const DEFAULT_ADDRESS = "г. Алматы, ул. Масанчи 86а";
  const year = new Date().getFullYear();

  const foot = document.getElementById("footCopy");
  if (foot) foot.textContent = `© ${year} SHIFT CYBER CLUB`;

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function money(n) {
    return `${Math.round(Number(n) || 0).toLocaleString("ru-RU")} ₸`;
  }

  function setBot(username) {
    const u = (username || DEFAULT_BOT).replace(/^@/, "").trim() || DEFAULT_BOT;
    const href = `https://t.me/${u}`;
    const miniApp = `https://t.me/${u}?startapp`;
    ["topTg", "ctaTg", "footTg"].forEach((id) => {
      const a = document.getElementById(id);
      if (!a) return;
      a.href = href;
      if (id === "ctaTg") a.textContent = `Открыть @${u}`;
    });
    const hint = document.getElementById("qrHint");
    if (hint) hint.textContent = `@${u}`;
    const stepBot = document.getElementById("stepBot");
    if (stepBot) {
      stepBot.innerHTML = `Найди бота <b>@${esc(u)}</b> или отсканируй QR справа.`;
    }
    const qr = document.getElementById("qr");
    if (qr) {
      qr.src = `/api/public/qr.png?d=${encodeURIComponent(miniApp)}&s=14`;
      qr.alt = `QR: @${u}`;
    }
  }

  function setAddress(addr) {
    const text = (addr && String(addr).trim()) || DEFAULT_ADDRESS;
    ["clubAddress", "footAddress"].forEach((id) => {
      const el = document.getElementById(id);
      if (el) el.textContent = text;
    });
  }

  async function loadClub() {
    try {
      const res = await fetch("/api/public/club", { cache: "no-store" });
      const json = await res.json();
      const d = json && json.data ? json.data : null;
      setBot(d && d.botUsername ? d.botUsername : DEFAULT_BOT);
      setAddress(d && d.address ? d.address : DEFAULT_ADDRESS);
      const brand = document.getElementById("clubBrand");
      if (brand && d && d.name) brand.textContent = d.name;
    } catch {
      setBot(DEFAULT_BOT);
      setAddress(DEFAULT_ADDRESS);
    }
  }

  async function loadFloor() {
    try {
      const res = await fetch("/api/public/floor", { cache: "no-store" });
      const json = await res.json();
      if (!json || !json.success || !json.data) return;
      const counts = json.data.counts || {};
      const set = (id, v) => {
        const el = document.getElementById(id);
        if (el) el.textContent = String(v ?? "—");
      };
      set("statFree", counts.free);
      set("statBusy", counts.busy);
      set("statTotal", counts.total);
    } catch {
      /* keep dashes */
    }
  }

  async function loadLoyalty() {
    try {
      const res = await fetch("/api/public/loyalty", { cache: "no-store" });
      const json = await res.json();
      const d = json && json.data ? json.data : null;
      if (!d) return;

      const perkBonus = document.getElementById("perkBonus");
      if (perkBonus && d.depositBonusEnabled && d.depositBonusTiers && d.depositBonusTiers.length) {
        const top = d.depositBonusTiers[d.depositBonusTiers.length - 1];
        perkBonus.textContent = `Например: пополнил от ${money(top.minAmount)} — получи +${money(top.bonusAmount)} бонусом. Уровни лояльности дают ещё больше.`;
      }

      const perkDiscount = document.getElementById("perkDiscount");
      if (perkDiscount && d.levels && d.levels.length) {
        const withDisc = d.levels.filter((l) => Number(l.timeDiscountPercent) > 0);
        if (withDisc.length) {
          const best = withDisc.reduce((a, b) =>
            Number(b.timeDiscountPercent) > Number(a.timeDiscountPercent) ? b : a,
          );
          perkDiscount.textContent = `До −${best.timeDiscountPercent}% на время на уровне «${best.name}». Чем больше играешь — тем выгоднее.`;
        }
      }
    } catch {
      /* keep defaults */
    }
  }

  loadClub();
  loadFloor();
  loadLoyalty();

  let floorTimer = setInterval(loadFloor, 12000);
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      clearInterval(floorTimer);
    } else {
      loadFloor();
      floorTimer = setInterval(loadFloor, 12000);
    }
  });
})();
