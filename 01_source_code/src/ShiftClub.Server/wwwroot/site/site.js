(() => {
  const year = new Date().getFullYear();
  const foot = document.getElementById("footCopy");
  if (foot) foot.textContent = `© ${year} SHIFT CYBER CLUB`;

  const DEFAULT_ADDRESS = "г. Алматы, ул. Масанчи 86а";
  const DEFAULT_BOT = "shift_panel_bot";

  const top = document.querySelector(".top");
  const onScroll = () => {
    if (!top) return;
    top.classList.toggle("is-solid", window.scrollY > 24);
  };
  onScroll();
  window.addEventListener("scroll", onScroll, { passive: true });

  function setBot(username) {
    const u = (username || DEFAULT_BOT).replace(/^@/, "").trim() || DEFAULT_BOT;
    const href = `https://t.me/${u}`;
    ["topTg", "heroTg", "ctaTg", "visitTg", "footTg"].forEach((id) => {
      const a = document.getElementById(id);
      if (!a) return;
      a.href = href;
      a.target = "_blank";
      a.rel = "noopener";
      if (id === "ctaTg") a.textContent = `Открыть @${u}`;
      if (id === "topTg" || id === "footTg") a.textContent = id === "topTg" ? "Telegram" : "Telegram";
      if (id === "heroTg") a.textContent = "Открыть бота";
      if (id === "visitTg") a.textContent = "Telegram-бот";
    });
  }

  async function loadClub() {
    try {
      const res = await fetch("/api/public/club", { cache: "no-store" });
      const json = await res.json();
      const d = json && json.data ? json.data : null;
      setBot(d && d.botUsername ? d.botUsername : DEFAULT_BOT);
      const addr = (d && d.address && String(d.address).trim()) || DEFAULT_ADDRESS;
      if (addr && addr !== "Almaty") {
        document.querySelectorAll(".visit h2").forEach((el) => {
          if (!el.dataset.locked) el.innerHTML = addr.replace(/,\s*/g, ",<br />");
        });
      }
    } catch (_) {
      setBot(DEFAULT_BOT);
    }
  }

  function occClass(o) {
    const s = String(o || "Free").toLowerCase();
    if (s === "free") return "is-free";
    if (s === "busy" || s === "paused") return "is-busy";
    if (s === "reserved") return "is-reserved";
    return "is-offline";
  }

  function occLabel(o) {
    const s = String(o || "").toLowerCase();
    if (s === "free") return "Свободен";
    if (s === "busy") return "Занят";
    if (s === "paused") return "Пауза";
    if (s === "reserved") return "Бронь";
    if (s === "offline") return "Офлайн";
    if (s === "maintenance") return "ТО";
    if (s === "updating") return "Обновление";
    if (s === "setup") return "Setup";
    return o || "—";
  }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function isWall(kind) {
    const k = String(kind ?? "").toLowerCase();
    return k === "wall" || k === "6";
  }

  /** Crop empty margins so the map isn't a sparse 28×14 on phones. */
  function computeCrop(pcs, elements, gridCols, gridRows) {
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
      if (isWall(e.kind)) return;
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

  function fitFloor() {
    const viewport = document.getElementById("floorViewport");
    const scaler = document.getElementById("floorScaler");
    const board = document.getElementById("floorBoard");
    if (!viewport || !scaler || !board || !board.children.length || viewport.hidden) return;

    scaler.style.transform = "scale(1)";
    scaler.style.width = "auto";
    scaler.style.height = "auto";

    const pad = 12;
    const vw = Math.max(0, viewport.clientWidth - pad);
    const vh = Math.max(0, viewport.clientHeight - pad);
    const bw = board.offsetWidth;
    const bh = board.offsetHeight;
    if (bw < 1 || bh < 1 || vw < 1 || vh < 1) return;

    const scale = Math.min(vw / bw, vh / bh);
    const s = Math.max(0.2, Math.min(1.25, scale));
    scaler.style.transform = `scale(${s})`;
  }

  function renderZonesFallback(pcs) {
    const host = document.getElementById("floorFallback");
    if (!host) return;
    const zones = {};
    pcs.forEach((pc) => {
      const z = (pc.zoneName && String(pc.zoneName).trim()) || "Зал";
      if (!zones[z]) zones[z] = [];
      zones[z].push(pc);
    });
    host.hidden = false;
    host.className = "floor-fallback-wrap";
    host.innerHTML = Object.keys(zones)
      .sort((a, b) => a.localeCompare(b, "ru"))
      .map((z) => {
        const list = zones[z].slice().sort((a, b) => String(a.name).localeCompare(String(b.name), "ru"));
        return `<div>
          <p class="eyebrow">${esc(z)}</p>
          <div class="floor-fallback">${list
            .map(
              (pc) => `<div class="pc-tile ${occClass(pc.occupancy)}">
              <strong>${esc(pc.name)}</strong>
              <span>${esc(occLabel(pc.occupancy))}</span>
            </div>`,
            )
            .join("")}</div>
        </div>`;
      })
      .join("");
  }

  /** Mobile-only: zone cards + free filter — no spatial grid. */
  function renderMobileHall(pcs, counts) {
    const host = document.getElementById("floorFallback");
    if (!host) return;

    const zones = {};
    pcs.forEach((pc) => {
      const z = (pc.zoneName && String(pc.zoneName).trim()) || "Зал";
      if (!zones[z]) zones[z] = { color: pc.zoneColorHex || "", list: [] };
      if (!zones[z].color && pc.zoneColorHex) zones[z].color = pc.zoneColorHex;
      zones[z].list.push(pc);
    });

    const zoneNames = Object.keys(zones).sort((a, b) => a.localeCompare(b, "ru"));
    const freeTotal = counts && counts.free != null ? counts.free : pcs.filter((p) => occClass(p.occupancy) === "is-free").length;

    host.hidden = false;
    host.className = "hall-mobile";
    host.innerHTML = `
      <div class="hall-mobile__bar">
        <strong>${freeTotal}</strong>
        <span>свободно сейчас</span>
      </div>
      <div class="hall-mobile__filters" role="tablist">
        <button type="button" class="hall-filter is-on" data-hall-filter="all">Все</button>
        <button type="button" class="hall-filter" data-hall-filter="free">Только свободные</button>
      </div>
      <div class="hall-mobile__zones">
        ${zoneNames
          .map((z) => {
            const pack = zones[z];
            const list = pack.list.slice().sort((a, b) => String(a.name).localeCompare(String(b.name), "ru"));
            const freeN = list.filter((p) => occClass(p.occupancy) === "is-free").length;
            const accent = pack.color ? `style="--z:${esc(pack.color)}"` : "";
            return `<section class="hall-zone" ${accent} data-free-count="${freeN}">
              <header class="hall-zone__head">
                <h3>${esc(z)}</h3>
                <span class="hall-zone__meta"><b>${freeN}</b> / ${list.length}</span>
              </header>
              <ul class="hall-zone__list">
                ${list
                  .map((pc) => {
                    const cls = occClass(pc.occupancy);
                    return `<li class="hall-pc ${cls}" data-free="${cls === "is-free" ? "1" : "0"}">
                      <span class="hall-pc__name">${esc(pc.name)}</span>
                      <span class="hall-pc__badge">${esc(occLabel(pc.occupancy))}</span>
                    </li>`;
                  })
                  .join("")}
              </ul>
            </section>`;
          })
          .join("")}
      </div>`;

    host.querySelectorAll("[data-hall-filter]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const mode = btn.getAttribute("data-hall-filter");
        host.querySelectorAll(".hall-filter").forEach((b) => b.classList.toggle("is-on", b === btn));
        host.querySelectorAll(".hall-pc").forEach((row) => {
          const show = mode === "all" || row.getAttribute("data-free") === "1";
          row.hidden = !show;
        });
        host.querySelectorAll(".hall-zone").forEach((sec) => {
          const visible = [...sec.querySelectorAll(".hall-pc")].some((r) => !r.hidden);
          sec.hidden = !visible;
        });
      });
    });
  }

  function renderFloor(map) {
    const board = document.getElementById("floorBoard");
    const status = document.getElementById("floorStatus");
    const fallback = document.getElementById("floorFallback");
    const viewport = document.getElementById("floorViewport");
    if (!board || !viewport) return;

    const counts = map.counts || {};
    const setStat = (id, v) => {
      const el = document.getElementById(id);
      if (el) el.textContent = String(v ?? "—");
    };
    setStat("statFree", counts.free);
    setStat("statBusy", counts.busy);
    setStat("statTotal", counts.total);

    const pcs = map.pcs || [];
    const elements = (map.elements || []).filter((e) => !isWall(e.kind));
    const gridCols = Math.max(4, Number(map.gridCols) || 12);
    const gridRows = Math.max(4, Number(map.gridRows) || 8);
    const placed = pcs.filter((p) => p.gridCol != null && p.gridRow != null);
    const narrow = window.matchMedia("(max-width: 820px)").matches;

    // Phones / tablets: zone list only — spatial map stays desktop
    if (narrow) {
      board.innerHTML = "";
      viewport.hidden = true;
      renderMobileHall(pcs, counts);
      if (status) {
        status.textContent = `Свободно ${counts.free ?? 0} из ${counts.total ?? pcs.length} · обновление каждые 8 сек`;
      }
      return;
    }

    if (placed.length === 0) {
      board.innerHTML = "";
      viewport.hidden = true;
      renderZonesFallback(pcs);
      if (status) status.textContent = "Карта без координат · список по зонам";
      return;
    }

    const crop = computeCrop(placed, elements, gridCols, gridRows);
    board.style.setProperty("--cols", String(crop.cols));
    board.style.setProperty("--rows", String(crop.rows));
    board.style.setProperty("--cell", "52px");

    const cells = [];
    elements.forEach((e) => {
      const col = Number(e.gridCol) - crop.originC + 1;
      const row = Number(e.gridRow) - crop.originR + 1;
      if (col < 1 || row < 1 || col > crop.cols || row > crop.rows) return;
      const cs = Math.max(1, Number(e.colSpan) || 1);
      const rs = Math.max(1, Number(e.rowSpan) || 1);
      cells.push(
        `<div class="floor-el" style="grid-column:${col}/span ${Math.min(cs, crop.cols - col + 1)};grid-row:${row}/span ${Math.min(
          rs,
          crop.rows - row + 1,
        )};${e.colorHex ? `background:${esc(e.colorHex)}33;` : ""}">${esc(e.label || e.kind || "")}</div>`,
      );
    });
    placed.forEach((pc) => {
      const col = Number(pc.gridCol) - crop.originC + 1;
      const row = Number(pc.gridRow) - crop.originR + 1;
      if (col < 1 || row < 1 || col > crop.cols || row > crop.rows) return;
      const border = pc.zoneColorHex ? `border-color:${esc(pc.zoneColorHex)};` : "";
      cells.push(
        `<div class="pc-tile ${occClass(pc.occupancy)}" style="grid-column:${col};grid-row:${row};${border}" title="${esc(
          pc.name,
        )} · ${esc(occLabel(pc.occupancy))}">
          <strong>${esc(pc.name)}</strong>
          <span>${esc(occLabel(pc.occupancy))}</span>
        </div>`,
      );
    });

    board.innerHTML = cells.join("");
    viewport.hidden = false;
    if (fallback) {
      fallback.hidden = true;
      fallback.innerHTML = "";
      fallback.className = "floor-fallback-wrap";
    }

    if (status) {
      status.textContent = `Свободно ${counts.free ?? 0} из ${counts.total ?? pcs.length} · обновление каждые 8 сек`;
    }

    requestAnimationFrame(() => {
      fitFloor();
      requestAnimationFrame(fitFloor);
    });
  }

  let floorTimer = 0;
  async function loadFloor() {
    try {
      const res = await fetch("/api/public/floor", { cache: "no-store" });
      const json = await res.json();
      if (!json || !json.success || !json.data) throw new Error("floor");
      renderFloor(json.data);
    } catch (_) {
      const status = document.getElementById("floorStatus");
      if (status) status.textContent = "Карта временно недоступна";
    }
  }

  let resizeTimer = 0;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      fitFloor();
      loadFloor();
    }, 140);
  });

  function money(n) {
    return `${Number(n || 0).toFixed(0)} ₸`;
  }

  function daysWord(n) {
    const x = Math.abs(Number(n) || 0) % 100;
    const d = x % 10;
    if (x > 10 && x < 20) return "дней";
    if (d === 1) return "день";
    if (d >= 2 && d <= 4) return "дня";
    return "дней";
  }

  async function loadLoyalty() {
    const lead = document.getElementById("loyaltyLead");
    const levelsEl = document.getElementById("loyalLevelsList");
    const depositEl = document.getElementById("loyalDepositList");
    const streakEl = document.getElementById("loyalStreakList");
    const bdayEl = document.getElementById("loyalBdayText");
    if (!levelsEl) return;

    try {
      const res = await fetch("/api/public/loyalty", { cache: "no-store" });
      const json = await res.json();
      const d = json && json.data;
      if (!d) throw new Error("loyalty");

      if (lead) {
        lead.textContent = `Актуальные условия ${d.clubName || "SHIFT CYBER CLUB"}. Меняются вместе с настройками в панели.`;
      }

      const levels = d.levels || [];
      levelsEl.innerHTML = levels.length
        ? levels
            .map((l) => {
              const bonus = Number(l.bonusPercent) || 0;
              const disc = Number(l.timeDiscountPercent) || 0;
              const min = Number(l.minSpent) || 0;
              const meta = min <= 0 ? "Стартовый уровень" : `От ${money(min)} трат`;
              const perks = [
                bonus > 0 ? `+${bonus}% к пополнению` : null,
                disc > 0 ? `−${disc}% ко времени` : null,
              ]
                .filter(Boolean)
                .join(" · ");
              return `<div class="loyal-row">
                <div><strong>${esc(l.name)}</strong><span class="loyal-code">${esc(l.code || "")}</span></div>
                <p>${esc(meta)}${perks ? " · " + esc(perks) : ""}</p>
              </div>`;
            })
            .join("")
        : `<p class="muted">Уровни пока не настроены.</p>`;

      if (depositEl) {
        if (!d.depositBonusEnabled) {
          depositEl.innerHTML = `<p class="muted">Сейчас выключен.</p>`;
        } else {
          const tiers = d.depositBonusTiers || [];
          depositEl.innerHTML = tiers.length
            ? tiers
                .map(
                  (t) => `<div class="loyal-row loyal-row--split">
                    <strong>от ${esc(money(t.minAmount))}</strong>
                    <b>+${esc(money(t.bonusAmount))}</b>
                  </div>`,
                )
                .join("")
            : `<p class="muted">Пороги не заданы.</p>`;
        }
      }

      if (streakEl) {
        if (!d.rewardsEnabled) {
          streakEl.innerHTML = `<p class="muted">Награды за серии выключены.</p>`;
        } else {
          const tiers = d.streakTiers || [];
          streakEl.innerHTML = tiers.length
            ? tiers
                .map((t) => {
                  const bar = Number(t.barRewards) > 0 ? ` · напиток ×${t.barRewards}` : "";
                  return `<div class="loyal-row loyal-row--split">
                    <strong>${t.days} ${daysWord(t.days)}</strong>
                    <b>${esc(money(t.bonusAmount))}${esc(bar)}</b>
                  </div>`;
                })
                .join("")
            : `<p class="muted">Пороги не заданы.</p>`;
        }
      }

      if (bdayEl) {
        if (!d.rewardsEnabled) {
          bdayEl.innerHTML = `<p class="muted">Подарок на день рождения выключен.</p>`;
        } else {
          const parts = [];
          if (Number(d.birthdayBonusAmount) > 0) parts.push(`<b>${esc(money(d.birthdayBonusAmount))}</b> бонусами`);
          if (Number(d.birthdayTimeBankMinutes) > 0)
            parts.push(`<b>${esc(String(d.birthdayTimeBankMinutes))} мин</b> в банк времени`);
          bdayEl.innerHTML = parts.length
            ? `<p>В день рождения: ${parts.join(" и ")}. Один раз в год.</p>`
            : `<p class="muted">Подарок не настроен.</p>`;
        }
      }
    } catch (_) {
      levelsEl.innerHTML = `<p class="muted">Не удалось загрузить условия.</p>`;
      if (depositEl) depositEl.innerHTML = "";
      if (streakEl) streakEl.innerHTML = "";
      if (bdayEl) bdayEl.innerHTML = "";
    }
  }

  async function loadPrice() {
    const grid = document.getElementById("tariffGrid");
    const promoEl = document.getElementById("pricePromo");
    const lead = document.getElementById("priceLead");
    const extrasRow = document.getElementById("tariffExtrasRow");
    if (!grid) return;

    try {
      const res = await fetch("/api/public/price", { cache: "no-store" });
      const json = await res.json();
      const d = json && json.data;
      if (!d || !Array.isArray(d.zones) || !d.zones.length) return;

      if (promoEl) {
        if (d.promo && d.promo.active) {
          promoEl.hidden = false;
          promoEl.innerHTML = `<strong>${esc(d.promo.label || "−" + d.promo.percent + "%")}</strong><span>${esc(d.promo.title || "Акция")} на все тарифы</span>`;
        } else {
          promoEl.hidden = true;
          promoEl.innerHTML = "";
        }
      }
      if (lead) {
        lead.textContent =
          d.promo && d.promo.active
            ? `${d.promo.label || "Скидка"} на все тарифы. Цены ниже уже со скидкой.`
            : "ПК по зонам, PlayStation 5 и кальян. Цены в тенге.";
      }

      grid.innerHTML = d.zones
        .map((z) => {
          const rows = (z.rows || [])
            .map((r) => {
              const sale = Number(r.price) || 0;
              const orig = r.originalPrice != null ? Number(r.originalPrice) : null;
              const priceHtml =
                orig != null && orig > sale
                  ? `<b><s>${esc(money(orig))}</s> ${esc(money(sale))}</b>`
                  : `<b>${esc(money(sale))}</b>`;
              const note = r.note ? ` ${esc(r.note)}` : "";
              return `<li><span>${esc(r.label)}${note ? `<em>${note}</em>` : ""}</span>${priceHtml}</li>`;
            })
            .join("");
          return `<article class="tariff" style="--c:${esc(z.color || "#ff6a00")}">
            <h3>${esc(z.name)}</h3>
            <ul>${rows}</ul>
          </article>`;
        })
        .join("");

      if (extrasRow && d.extras && Array.isArray(d.extras.items)) {
        extrasRow.innerHTML = d.extras.items
          .map((it) => {
            const note = it.note ? ` <em>${esc(it.note)}</em>` : "";
            return `<li><span>${esc(it.label)}${note}</span><b>${esc(money(it.price))}</b></li>`;
          })
          .join("");
      }
    } catch {
      /* keep fallback HTML */
    }
  }

  setBot(DEFAULT_BOT);
  loadClub();
  loadLoyalty();
  loadPrice();
  loadFloor();
  floorTimer = setInterval(loadFloor, 8000);

  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      clearInterval(floorTimer);
    } else {
      loadFloor();
      loadLoyalty();
      loadPrice();
      floorTimer = setInterval(loadFloor, 8000);
    }
  });
})();
