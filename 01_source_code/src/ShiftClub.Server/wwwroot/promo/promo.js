(() => {
  const DEFAULT_BOT = "shift_panel_bot";
  const REVIEW_URL = "https://go.2gis.com/YszAd";
  const FIRM_REVIEWS =
    "https://2gis.kz/almaty/firm/70000001114343411/tab/reviews";

  function money(n) {
    return `${Math.round(Number(n) || 0).toLocaleString("ru-RU")} ₸`;
  }

  function qrSrc(payload, size = 18) {
    return `/api/public/qr.png?d=${encodeURIComponent(payload)}&s=${size}`;
  }

  function setQr(img, payload, captionEl, captionText, size = 18) {
    if (!img || !payload) return;
    img.src = qrSrc(payload, size);
    img.width = 512;
    img.height = 512;
    img.alt = `QR: ${payload}`;
    if (captionEl && captionText) captionEl.textContent = captionText;
  }

  function botLink(username, startApp) {
    const u = String(username || DEFAULT_BOT).replace(/^@/, "").trim() || DEFAULT_BOT;
    const start = startApp ? `?startapp=${encodeURIComponent(String(startApp))}` : "?startapp";
    return {
      username: u,
      botUrl: `https://t.me/${u}`,
      /** Открывает Mini App, если он привязан к боту. */
      miniAppUrl: `https://t.me/${u}${start}`,
    };
  }

  async function loadClub() {
    try {
      const res = await fetch("/api/public/club", { cache: "no-store" });
      const json = await res.json();
      return json && json.data ? json.data : null;
    } catch {
      return null;
    }
  }

  async function loadLoyalty() {
    try {
      const res = await fetch("/api/public/loyalty", { cache: "no-store" });
      const json = await res.json();
      return json && json.data ? json.data : null;
    } catch {
      return null;
    }
  }

  async function loadCase() {
    try {
      const res = await fetch("/api/public/case", { cache: "no-store" });
      const json = await res.json();
      return json && json.data ? json.data : null;
    } catch {
      return null;
    }
  }

  function fillAddress(el, club) {
    if (!el) return;
    const addr = (club && club.address && String(club.address).trim()) || "г. Алматы, ул. Масанчи 86а";
    el.textContent = addr;
  }

  window.ShiftPromo = {
    DEFAULT_BOT,
    REVIEW_URL,
    FIRM_REVIEWS,
    money,
    qrSrc,
    setQr,
    botLink,
    loadClub,
    loadLoyalty,
    loadCase,
    fillAddress,
  };
})();
