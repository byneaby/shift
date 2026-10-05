(() => {
  const TYPE_CLASS = {
    1: "t-time",
    2: "t-balance",
    3: "t-bar",
    4: "t-discount",
    5: "t-service",
    9: "t-service",
  };

  const SPIN_MS = 7200;
  const SPIN_CARDS = 38;
  const GAP = 12;
  const TOKEN_KEY = "shiftclub.token";
  const DESK_CHANNEL = "shift.deskDisplay";
  const DESK_LS_KEY = "shift.deskDisplay.cmd";
  /** Сколько держать карточку приза до закрытия оверлея / возврата на кассу. */
  const RESULT_HOLD_MS = 10000;
  const AUTO_OPEN_MS = 450;

  const params = new URLSearchParams(location.search || "");
  const isDesk = params.get("desk") === "1";
  const isEmbed = params.get("embed") === "1" || params.get("from") === "upgrade";
  const displayToken = (params.get("token") || "").trim();
  if (!isDesk) {
    location.replace("/site/");
    return;
  }

  const state = {
    prizes: [],
    spinning: false,
    seq: [],
    index: 0,
    audio: null,
    guest: null, // { customerId, displayName, phone, keysBalance, keyCost }
    searchTimer: null,
    embed: isEmbed,
    displayToken,
    deskCommandId: params.get("cmd") || "",
    lastWin: null,
    holdTimer: null,
    holdTick: null,
    holdUntil: 0,
    autoOpenTimer: null,
    deskCommandBusy: false,
  };

  const el = {
    track: document.getElementById("rouletteTrack"),
    openBtn: document.getElementById("openBtn"),
    openCost: document.getElementById("openCost"),
    status: document.getElementById("statusLine"),
    prizeGrid: document.getElementById("prizeGrid"),
    winModal: document.getElementById("winModal"),
    winImg: document.getElementById("winImg"),
    winName: document.getElementById("winName"),
    winDesc: document.getElementById("winDesc"),
    winClose: document.getElementById("winClose"),
    guestQuery: document.getElementById("guestQuery"),
    guestResults: document.getElementById("guestResults"),
    guestSelected: document.getElementById("guestSelected"),
    guestName: document.getElementById("guestName"),
    guestPhone: document.getElementById("guestPhone"),
    guestKeys: document.getElementById("guestKeys"),
    guestClear: document.getElementById("guestClear"),
    deskHint: document.getElementById("deskHint"),
    deskBack: document.getElementById("deskBack"),
  };

  function staffToken() {
    return localStorage.getItem(TOKEN_KEY) || "";
  }

  function ensureAudio() {
    if (state.audio) return state.audio;
    const Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) return null;
    const ctx = new Ctx();
    state.audio = {
      ctx,
      tick(progress) {
        if (ctx.state === "suspended") ctx.resume().catch(() => {});
        const t0 = ctx.currentTime;
        const dur = 0.035;
        const buf = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * dur), ctx.sampleRate);
        const data = buf.getChannelData(0);
        for (let i = 0; i < data.length; i++) {
          const env = 1 - i / data.length;
          data[i] = (Math.random() * 2 - 1) * env * env;
        }
        const src = ctx.createBufferSource();
        src.buffer = buf;
        const bp = ctx.createBiquadFilter();
        bp.type = "bandpass";
        bp.frequency.value = 1400 + progress * 900;
        bp.Q.value = 2.2;
        const g = ctx.createGain();
        g.gain.setValueAtTime(0.22, t0);
        g.gain.exponentialRampToValueAtTime(0.001, t0 + dur);
        src.connect(bp);
        bp.connect(g);
        g.connect(ctx.destination);
        src.start(t0);
      },
      reveal() {
        if (ctx.state === "suspended") ctx.resume().catch(() => {});
        const t0 = ctx.currentTime;
        [523.25, 784.99, 1046.5].forEach((freq, i) => {
          const osc = ctx.createOscillator();
          const g = ctx.createGain();
          osc.type = "sine";
          osc.frequency.value = freq;
          const start = t0 + i * 0.07;
          g.gain.setValueAtTime(0.0001, start);
          g.gain.exponentialRampToValueAtTime(0.12, start + 0.02);
          g.gain.exponentialRampToValueAtTime(0.0001, start + 0.45);
          osc.connect(g);
          g.connect(ctx.destination);
          osc.start(start);
          osc.stop(start + 0.5);
        });
      },
    };
    return state.audio;
  }

  async function api(path, opts = {}) {
    const headers = { Accept: "application/json" };
    if (opts.body) headers["Content-Type"] = "application/json";
    // Публичный токен экрана — без логина кассы
    const usePublic = !!opts.public || path.startsWith("/api/public/");
    if (!usePublic) {
      const token = staffToken();
      if (token) headers.Authorization = `Bearer ${token}`;
    }
    const res = await fetch(path, { ...opts, headers, cache: "no-store" });
    const json = await res.json().catch(() => ({}));
    if (res.status === 401 && !usePublic) {
      throw new Error("Нужен вход в панель кассы (открой /login в этой же вкладке браузера).");
    }
    if (!res.ok || json.success === false) throw new Error(json.message || `HTTP ${res.status}`);
    return json.data;
  }

  function esc(s) {
    return String(s || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function typeClass(t) {
    return TYPE_CLASS[t] || "t-service";
  }

  function buildChip(p) {
    const div = document.createElement("div");
    div.className = `chip ${typeClass(p.prizeType)}`;
    div.dataset.code = p.prizeCode;
    div.innerHTML = `<img src="${esc(p.imageUrl || "")}" alt="" /><span>${esc(p.name)}</span>`;
    return div;
  }

  function metrics() {
    const item = el.track.children[0];
    if (!item) return { step: 124, centerX: 0, itemW: 112 };
    const itemW = item.getBoundingClientRect().width;
    const step = itemW + GAP;
    const viewport = el.track.parentElement.getBoundingClientRect().width;
    const centerX = viewport / 2 - itemW / 2;
    return { step, centerX, itemW };
  }

  function xForIndex(index) {
    const { step, centerX } = metrics();
    return centerX - index * step;
  }

  function setTrackX(x, withTransition) {
    el.track.style.transition = withTransition || "none";
    el.track.style.transform = `translate3d(${x}px,0,0)`;
  }

  function fillTrack(prizes, loops = 12) {
    el.track.innerHTML = "";
    const seq = [];
    for (let i = 0; i < loops; i++) {
      seq.push(...[...prizes].sort(() => Math.random() - 0.5));
    }
    seq.forEach((p) => el.track.appendChild(buildChip(p)));
    state.seq = seq;
    return seq;
  }

  function appendBatch(prizes) {
    const batch = [...prizes].sort(() => Math.random() - 0.5);
    batch.forEach((p) => {
      state.seq.push(p);
      el.track.appendChild(buildChip(p));
    });
  }

  function ensureRunway(minLength) {
    while (state.seq.length < minLength) appendBatch(state.prizes);
  }

  function showIdleRandom() {
    if (!state.seq.length) return;
    const min = Math.min(8, state.seq.length - 1);
    const max = Math.max(min, Math.min(state.seq.length - 8, Math.floor(state.seq.length * 0.35)));
    const idx = min + Math.floor(Math.random() * Math.max(1, max - min + 1));
    state.index = idx;
    void el.track.offsetWidth;
    setTrackX(xForIndex(idx), "none");
  }

  function easeOutQuint(t) {
    return 1 - Math.pow(1 - t, 5);
  }

  function animateToIndex(fromIndex, toIndex) {
    const startX = xForIndex(fromIndex);
    const endX = xForIndex(toIndex);
    const audio = ensureAudio();
    let lastTick = fromIndex;
    const t0 = performance.now();

    return new Promise((resolve) => {
      setTrackX(startX, "none");

      function frame(now) {
        const raw = Math.min(1, (now - t0) / SPIN_MS);
        const e = easeOutQuint(raw);
        const x = startX + (endX - startX) * e;
        setTrackX(x, "none");

        const { step, centerX } = metrics();
        const current = Math.round((centerX - x) / step);
        if (current !== lastTick && current >= 0 && current < state.seq.length) {
          lastTick = current;
          audio?.tick(raw);
        }

        if (raw < 1) requestAnimationFrame(frame);
        else {
          setTrackX(endX, "none");
          state.index = toIndex;
          resolve();
        }
      }

      requestAnimationFrame(frame);
    });
  }

  function renderLoot(prizes) {
    el.prizeGrid.innerHTML = prizes
      .map(
        (p) => `<article class="prize-card ${typeClass(p.prizeType)}">
          <img src="${esc(p.imageUrl || "")}" alt="" />
          <strong>${esc(p.name)}</strong>
        </article>`,
      )
      .join("");
  }

  function showWin(res) {
    state.lastWin = res;
    el.winImg.src = res.imageUrl || "";
    el.winName.textContent = res.prizeName || "";
    el.winDesc.textContent = res.applyMessage || res.description || "";
    el.winModal.hidden = false;
    document.body.style.overflow = "hidden";
    ensureAudio()?.reveal();
    startResultHold();
  }

  function clearResultHold() {
    if (state.holdTimer) {
      clearTimeout(state.holdTimer);
      state.holdTimer = null;
    }
    if (state.holdTick) {
      clearInterval(state.holdTick);
      state.holdTick = null;
    }
    state.holdUntil = 0;
  }

  function updateHoldButton() {
    if (!el.winClose) return;
    const left = Math.max(0, Math.ceil((state.holdUntil - Date.now()) / 1000));
    if (left > 0) {
      el.winClose.textContent = `На кассу через ${left} с`;
      el.winClose.disabled = true;
    } else {
      el.winClose.textContent = "На кассу · Enter";
      el.winClose.disabled = false;
    }
  }

  function startResultHold() {
    clearResultHold();
    state.holdUntil = Date.now() + RESULT_HOLD_MS;
    updateHoldButton();
    state.holdTick = setInterval(updateHoldButton, 250);
    state.holdTimer = setTimeout(() => {
      finishAfterResult();
    }, RESULT_HOLD_MS);
  }

  function finishAfterResult() {
    clearResultHold();
    el.winModal.hidden = true;
    document.body.style.overflow = "";
    notifyDone(state.lastWin);
    // Касса / отдельная вкладка: после показа приза — обратно к клиентам
    if (!state.embed) {
      const cid = state.guest && state.guest.customerId ? String(state.guest.customerId) : "";
      const prize = state.lastWin && state.lastWin.prizeName ? String(state.lastWin.prizeName) : "";
      const q = new URLSearchParams();
      if (cid) q.set("c", cid);
      if (prize) q.set("casePrize", prize);
      if (state.lastWin && state.lastWin.imageUrl) q.set("caseImg", String(state.lastWin.imageUrl));
      if (state.lastWin && state.lastWin.applyMessage) q.set("caseMsg", String(state.lastWin.applyMessage));
      if (state.lastWin && state.lastWin.rewardId) q.set("caseRewardId", String(state.lastWin.rewardId));
      if (state.lastWin && state.lastWin.prizeType != null) q.set("caseType", String(state.lastWin.prizeType));
      if (state.lastWin && state.lastWin.rewardStatus != null) q.set("caseStatus", String(state.lastWin.rewardStatus));
      if (state.lastWin && state.lastWin.payloadJson) q.set("casePayload", String(state.lastWin.payloadJson));
      location.href = `/customers${q.toString() ? `?${q}` : ""}`;
    }
  }

  function closeWin() {
    // Пока идёт таймер 10 с — не закрываем раньше
    if (state.holdUntil && Date.now() < state.holdUntil) return;
    finishAfterResult();
  }

  function notifyDone(win) {
    try {
      if (window.parent && window.parent !== window) {
        window.parent.postMessage(
          {
            type: "shift.case.done",
            prizeName: win && win.prizeName,
            prizeCode: win && win.prizeCode,
            imageUrl: win && win.imageUrl,
            applyMessage: win && win.applyMessage,
            customerId: state.guest && state.guest.customerId,
            keysRemaining: win && win.keysRemaining,
          },
          "*",
        );
      }
    } catch {
      /* ignore */
    }
    const cmd = state.deskCommandId || params.get("cmd");
    if (cmd) {
      fetch(`/api/public/desk-display/ack?commandId=${encodeURIComponent(cmd)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: "{}",
      }).catch(() => {});
    }
  }

  function isModalOpen() {
    return el.winModal && !el.winModal.hidden;
  }

  function syncOpenBtn() {
    const g = state.guest;
    const ok = !!(g && g.keysBalance >= (g.keyCost || 1) && !state.spinning);
    el.openBtn.disabled = !ok;
    if (el.openCost) {
      el.openCost.textContent = g ? `${g.keysBalance} ключ` : "ключ";
    }
    if (!g) {
      el.deskHint.textContent = "Найди гостя — затем ОТКРЫТЬ. Нужен ключ (за регистрацию).";
    } else if (g.keysBalance < (g.keyCost || 1)) {
      el.deskHint.textContent = "У гостя нет ключей. Ключ даётся за создание аккаунта.";
    } else {
      el.deskHint.textContent = `Готов: ${g.displayName}. Enter — открыть кейс.`;
    }
  }

  function renderGuestSelected() {
    const g = state.guest;
    if (!g) {
      el.guestSelected.hidden = true;
      el.guestQuery.closest(".desk-guest__search").hidden = false;
      syncOpenBtn();
      return;
    }
    el.guestSelected.hidden = false;
    el.guestQuery.closest(".desk-guest__search").hidden = true;
    el.guestResults.hidden = true;
    el.guestName.textContent = g.displayName;
    el.guestPhone.textContent = g.phone || "";
    el.guestKeys.textContent = String(g.keysBalance);
    syncOpenBtn();
  }

  function pickDeskGuest(desk) {
    return {
      customerId: desk.customerId ?? desk.CustomerId,
      displayName: desk.displayName ?? desk.DisplayName ?? "",
      phone: desk.phone ?? desk.Phone ?? "",
      keysBalance: Number(desk.keysBalance ?? desk.KeysBalance ?? 0),
      keyCost: Number(desk.keyCost ?? desk.KeyCost ?? 1) || 1,
    };
  }

  function guestCanOpen(g) {
    return !!(g && g.keysBalance >= (g.keyCost || 1));
  }

  function scheduleAutoOpen(force = false) {
    if (state.autoOpenTimer) clearTimeout(state.autoOpenTimer);
    state.autoOpenTimer = setTimeout(() => {
      state.autoOpenTimer = null;
      if (state.spinning || !state.guest || !state.prizes.length) return;
      if (!force && !guestCanOpen(state.guest)) return;
      void openCase(true);
    }, AUTO_OPEN_MS);
  }

  async function selectGuest(customerId) {
    const desk = state.displayToken
      ? await api(`/api/public/desk-case/${encodeURIComponent(state.displayToken)}`, { public: true })
      : await api(`/api/cases/desk/${customerId}`);
    state.guest = pickDeskGuest(desk);
    el.guestQuery.value = "";
    renderGuestSelected();
    el.status.textContent = "";
  }

  function parseDeskCommand(raw) {
    if (!raw) return null;
    let data = raw;
    if (typeof raw === "string") {
      try {
        data = JSON.parse(raw);
      } catch {
        return null;
      }
    }
    const customerId = data.customerId || data.CustomerId;
    if (!customerId) return null;
    return {
      customerId: String(customerId),
      commandId: String(data.commandId || data.CommandId || ""),
      displayToken: String(data.displayToken || data.DisplayToken || "").trim(),
    };
  }

  async function handleDeskCommand(raw) {
    const cmd = parseDeskCommand(raw);
    if (!cmd) return;
    if (cmd.commandId && cmd.commandId === state.deskCommandId && state.spinning) return;
    if (state.deskCommandBusy) return;

    state.deskCommandBusy = true;
    state.deskCommandId = cmd.commandId || state.deskCommandId;
    if (cmd.displayToken) state.displayToken = cmd.displayToken;

    const bar = document.getElementById("deskGuestBar");
    if (bar) {
      const search = bar.querySelector(".desk-guest__search");
      if (search) search.hidden = true;
    }

    try {
      await selectGuest(cmd.customerId);
      el.deskHint.textContent = "Гость с кассы — крутим кейс…";
      scheduleAutoOpen(true);
    } catch (e) {
      el.status.textContent = e.message || "Гость не загружен";
    } finally {
      state.deskCommandBusy = false;
    }
  }

  function listenDeskCommands() {
    try {
      const bc = new BroadcastChannel(DESK_CHANNEL);
      bc.onmessage = (ev) => void handleDeskCommand(ev.data);
    } catch {
      /* old browser */
    }
    window.addEventListener("storage", (ev) => {
      if (ev.key === DESK_LS_KEY && ev.newValue) void handleDeskCommand(ev.newValue);
    });
  }

  async function searchGuests(q) {
    const query = String(q || "").trim();
    if (query.length < 2) {
      el.guestResults.hidden = true;
      el.guestResults.innerHTML = "";
      return;
    }
    const list = (await api(`/api/customers?q=${encodeURIComponent(query)}`)) || [];
    if (!list.length) {
      el.guestResults.hidden = false;
      el.guestResults.innerHTML = `<div class="desk-guest__hint">Никого не найдено</div>`;
      return;
    }
    el.guestResults.hidden = false;
    el.guestResults.innerHTML = list
      .slice(0, 8)
      .map(
        (c) => `<button type="button" class="desk-guest__hit" data-id="${esc(c.id)}">
          <span><strong>${esc(c.fullName || c.firstName || "")}</strong><br/><small class="muted">${esc(c.phone || "")}</small></span>
        </button>`,
      )
      .join("");
    el.guestResults.querySelectorAll("[data-id]").forEach((btn) => {
      btn.onclick = () => void selectGuest(btn.getAttribute("data-id"));
    });
  }

  async function openCase(force = false) {
    if (state.spinning || !state.prizes.length) return;
    if (!force && el.openBtn.disabled) return;
    if (!state.guest) {
      el.status.textContent = "Сначала выбери гостя";
      el.guestQuery?.focus();
      return;
    }
    if (isModalOpen()) {
      closeWin();
      return;
    }

    ensureAudio();
    state.spinning = true;
    el.openBtn.disabled = true;
    el.status.textContent = "";

    try {
      const startIndex = Math.max(0, state.index || 0);
      setTrackX(xForIndex(startIndex), "none");

      const res = state.displayToken
        ? await api(`/api/public/desk-case/${encodeURIComponent(state.displayToken)}/open`, {
            method: "POST",
            body: "{}",
            public: true,
          })
        : await api(`/api/cases/open/${state.guest.customerId}`, {
            method: "POST",
            body: JSON.stringify({ idempotencyKey: `desk-${state.guest.customerId}-${Date.now()}` }),
          });

      const jitter = Math.floor(Math.random() * Math.min(5, state.prizes.length || 1));
      const targetIndex = startIndex + SPIN_CARDS + jitter;
      ensureRunway(targetIndex + 8);

      state.seq[targetIndex] = {
        prizeCode: res.prizeCode,
        name: res.prizeName,
        prizeType: res.prizeType,
        imageUrl: res.imageUrl,
        description: res.applyMessage || "",
      };
      const node = el.track.children[targetIndex];
      if (node) {
        node.dataset.code = res.prizeCode;
        node.className = `chip ${typeClass(res.prizeType)}`;
        node.innerHTML = `<img src="${esc(res.imageUrl || "")}" alt="" /><span>${esc(res.prizeName)}</span>`;
      }

      const { step } = metrics();
      const landJitter = (Math.random() - 0.5) * step * 0.22;

      await animateToIndex(startIndex, targetIndex);
      if (Math.abs(landJitter) > 1) {
        const finalX = xForIndex(targetIndex) + landJitter;
        el.track.style.transition = "transform 320ms cubic-bezier(0.2, 0.8, 0.2, 1)";
        el.track.style.transform = `translate3d(${finalX}px,0,0)`;
        await new Promise((r) => setTimeout(r, 340));
        el.track.style.transition = "none";
      }

      state.guest.keysBalance = res.keysRemaining ?? Math.max(0, state.guest.keysBalance - 1);
      renderGuestSelected();
      showWin(res);
    } catch (e) {
      el.status.textContent = e.message || "Не удалось открыть";
      showIdleRandom();
      syncOpenBtn();
    } finally {
      state.spinning = false;
      syncOpenBtn();
    }
  }

  async function load() {
    if (!state.displayToken && !staffToken()) {
      el.openBtn.disabled = true;
      el.status.textContent = "Войди в панель кассы (/login), затем открой кейс снова.";
      el.deskHint.textContent = "Нужна авторизация сотрудника в этом браузере.";
    }

    const pub = await api("/api/public/case", { public: true });
    if (!pub.catalog?.isEnabled) {
      el.openBtn.disabled = true;
      el.status.textContent = "Рулетка сейчас недоступна";
      return;
    }
    state.prizes = pub.catalog?.prizes || [];
    renderLoot(state.prizes);
    fillTrack(state.prizes, 10);
    requestAnimationFrame(() => showIdleRandom());

    const autoStart =
      state.displayToken || params.get("autostart") === "1" || !!params.get("cmd");

    if (autoStart) {
      const bar = document.getElementById("deskGuestBar");
      if (bar) {
        const search = bar.querySelector(".desk-guest__search");
        if (search) search.hidden = true;
      }
      try {
        await selectGuest(params.get("customerId") || "");
        el.deskHint.textContent = "Гость с кассы — крутим кейс…";
        scheduleAutoOpen(true);
      } catch (e) {
        el.status.textContent = e.message || "Гость не загружен";
      }
    } else {
      const presetId = params.get("customerId") || params.get("c");
      if (presetId) {
        try {
          await selectGuest(presetId);
        } catch (e) {
          el.status.textContent = e.message || "Гость не загружен";
        }
      } else {
        syncOpenBtn();
      }
    }
  }

  el.openBtn.addEventListener("click", () => void openCase());
  el.winClose.addEventListener("click", closeWin);
  // Веер не закрывает раньше таймера — только кнопка после 10 с / Enter
  el.guestClear?.addEventListener("click", () => {
    state.guest = null;
    renderGuestSelected();
    el.guestQuery.focus();
  });
  if (state.embed && el.deskBack) {
    el.deskBack.hidden = false;
    document.body.classList.add("embed-case");
    el.deskBack.addEventListener("click", () => {
      if (state.holdUntil && Date.now() < state.holdUntil) return;
      notifyDone(state.lastWin);
    });
  }
  el.guestQuery?.addEventListener("input", () => {
    clearTimeout(state.searchTimer);
    state.searchTimer = setTimeout(() => {
      searchGuests(el.guestQuery.value).catch((e) => {
        el.status.textContent = e.message || "Поиск не удался";
      });
    }, 220);
  });

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape") {
      closeWin();
      return;
    }
    if (e.key !== "Enter") return;
    const tag = (e.target && e.target.tagName) || "";
    if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") {
      if (e.target === el.guestQuery && el.guestResults && !el.guestResults.hidden) {
        const first = el.guestResults.querySelector("[data-id]");
        if (first) {
          e.preventDefault();
          first.click();
        }
      }
      return;
    }
    e.preventDefault();
    if (isModalOpen()) closeWin();
    else void openCase();
  });

  window.addEventListener("resize", () => {
    if (!state.spinning && state.seq.length) setTrackX(xForIndex(state.index), "none");
  });

  listenDeskCommands();
  load().catch((e) => {
    el.status.textContent = e.message || "Ошибка загрузки";
    el.openBtn.disabled = true;
  });
})();
