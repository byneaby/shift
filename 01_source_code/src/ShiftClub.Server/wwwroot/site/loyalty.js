(() => {
  const money = (n) => `${Number(n || 0).toFixed(0)} ₸`;

  function el(id) {
    return document.getElementById(id);
  }

  function renderLevels(levels) {
    const box = el("levels");
    if (!levels || !levels.length) {
      box.innerHTML = `<div class="off">Уровни пока не настроены.</div>`;
      return;
    }
    box.className = "stack";
    box.innerHTML = levels
      .map((l) => {
        const bonus = Number(l.bonusPercent) || 0;
        const disc = Number(l.timeDiscountPercent) || 0;
        const min = Number(l.minSpent) || 0;
        const meta =
          min <= 0
            ? "Стартовый уровень"
            : `От ${money(min)} трат в клубе`;
        const perks = [
          bonus > 0 ? `+${bonus}% к пополнению` : null,
          disc > 0 ? `−${disc}% к цене времени` : null,
        ]
          .filter(Boolean)
          .join(" · ");
        return `<article class="tier">
          <strong>${escapeHtml(l.name)}</strong>
          <span class="badge">${escapeHtml(l.code || "")}</span>
          <div class="meta">${escapeHtml(meta)}${perks ? " · " + escapeHtml(perks) : ""}</div>
        </article>`;
      })
      .join("");
  }

  function renderStreaks(enabled, tiers) {
    const box = el("streaks");
    const card = el("streaksCard");
    if (!enabled) {
      box.innerHTML = `<div class="off">Награды за серии визитов сейчас выключены.</div>`;
      return;
    }
    if (!tiers || !tiers.length) {
      box.innerHTML = `<div class="muted">Пороги серии не заданы.</div>`;
      return;
    }
    box.className = "stack";
    box.innerHTML = tiers
      .map((t) => {
        const bar =
          Number(t.barRewards) > 0
            ? ` · ${t.barRewards} напиток из бара`
            : "";
        return `<article class="tier">
          <strong>${t.days} ${daysWord(t.days)}</strong>
          <span class="badge">${money(t.bonusAmount)}</span>
          <div class="meta">Бонус на счёт${escapeHtml(bar)}</div>
        </article>`;
      })
      .join("");
    card.querySelector(".hint")?.removeAttribute("hidden");
  }

  function renderBday(enabled, bonus, minutes) {
    const box = el("bday");
    if (!enabled) {
      box.innerHTML = `<div class="off">Подарок на день рождения выключен.</div>`;
      return;
    }
    const parts = [];
    if (Number(bonus) > 0) parts.push(`<em>${money(bonus)}</em> бонусами`);
    if (Number(minutes) > 0) parts.push(`<em>${minutes} мин</em> в банк времени`);
    box.className = "bday";
    box.innerHTML = parts.length
      ? `В день рождения на аккаунт: ${parts.join(" и ")}. Один раз в год.`
      : `<span class="muted">Подарок на день рождения не настроен.</span>`;
  }

  function daysWord(n) {
    const x = Math.abs(n) % 100;
    const d = x % 10;
    if (x > 10 && x < 20) return "дней";
    if (d === 1) return "день";
    if (d >= 2 && d <= 4) return "дня";
    return "дней";
  }

  function escapeHtml(s) {
    return String(s || "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function renderDeposit(enabled, tiers) {
    const box = el("deposit");
    if (!enabled) {
      box.innerHTML = `<div class="off">Бонус за пополнение сейчас выключен.</div>`;
      return;
    }
    if (!tiers || !tiers.length) {
      box.innerHTML = `<div class="muted">Пороги не заданы.</div>`;
      return;
    }
    box.className = "stack";
    box.innerHTML = tiers
      .map((t) => {
        return `<article class="tier">
          <strong>от ${money(t.minAmount)}</strong>
          <span class="badge">+${money(t.bonusAmount)}</span>
          <div class="meta">На бонусный счёт при пополнении</div>
        </article>`;
      })
      .join("");
  }

  async function load() {
    try {
      const res = await fetch("/api/public/loyalty");
      const json = await res.json();
      const d = json && json.data;
      if (!d) throw new Error("no data");
      el("lead").textContent = `Актуальные условия ${d.clubName || "SHIFT"}. Меняются вместе с настройками в панели.`;
      el("foot").textContent = `© ${new Date().getFullYear()} ${d.clubName || "SHIFT Club"}`;
      document.title = `Лояльность и награды — ${d.clubName || "SHIFT Club"}`;
      renderLevels(d.levels || []);
      renderDeposit(!!d.depositBonusEnabled, d.depositBonusTiers || []);
      renderStreaks(!!d.rewardsEnabled, d.streakTiers || []);
      renderBday(!!d.rewardsEnabled, d.birthdayBonusAmount, d.birthdayTimeBankMinutes);
    } catch (_) {
      el("levels").innerHTML = `<div class="off">Не удалось загрузить условия. Обновите страницу.</div>`;
      el("deposit").textContent = "";
      el("streaks").textContent = "";
      el("bday").textContent = "";
    }
  }

  load();
})();
