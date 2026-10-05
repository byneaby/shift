(() => {
  const TOKEN_KEY = "shift_work_token";
  const ME_KEY = "shift_work_me";

  const STATUS = {
    Scheduled: { label: "План", cls: "" },
    Working: { label: "На работе", cls: "ok" },
    OnBreak: { label: "Перерыв", cls: "blue" },
    Completed: { label: "Ушёл", cls: "" },
    Absent: { label: "Неявка", cls: "bad" },
    Late: { label: "Опоздание", cls: "warn" },
    Cancelled: { label: "Отмена", cls: "bad" },
    0: { label: "План", cls: "" },
    1: { label: "На работе", cls: "ok" },
    2: { label: "Перерыв", cls: "blue" },
    3: { label: "Ушёл", cls: "" },
    4: { label: "Неявка", cls: "bad" },
    5: { label: "Опоздание", cls: "warn" },
    6: { label: "Отмена", cls: "bad" },
  };

  const WEEKDAYS = [
    { v: 1, t: "Пн" },
    { v: 2, t: "Вт" },
    { v: 3, t: "Ср" },
    { v: 4, t: "Чт" },
    { v: 5, t: "Пт" },
    { v: 6, t: "Сб" },
    { v: 0, t: "Вс" },
  ];

  const app = document.getElementById("app");
  const topActions = document.getElementById("topActions");

  let token = localStorage.getItem(TOKEN_KEY) || "";
  let me = null;
  let tab = "today";
  let boardDate = todayAlmaty();
  let flash = "";
  let error = "";
  let busy = false;
  let board = null;
  let myShifts = [];
  let employees = [];
  let adminShifts = [];
  let noteDrafts = {};

  try {
    me = JSON.parse(localStorage.getItem(ME_KEY) || "null");
  } catch {
    me = null;
  }

  function todayAlmaty() {
    return new Intl.DateTimeFormat("en-CA", {
      timeZone: "Asia/Almaty",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    }).format(new Date());
  }

  function fmtTime(v) {
    if (!v) return "—";
    const s = String(v);
    return s.length >= 5 ? s.slice(0, 5) : s;
  }

  function fmtPlan(start, end) {
    const a = fmtTime(start);
    const b = fmtTime(end);
    const toMin = (t) => {
      const p = String(t || "").split(":");
      return Number(p[0] || 0) * 60 + Number(p[1] || 0);
    };
    if (toMin(end) < toMin(start)) return `${a}–${b} (+1 день)`;
    return `${a}–${b}`;
  }

  function fmtWhen(iso) {
    if (!iso) return "—";
    try {
      return new Intl.DateTimeFormat("ru-RU", {
        timeZone: "Asia/Almaty",
        day: "2-digit",
        month: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
      }).format(new Date(iso));
    } catch {
      return iso;
    }
  }

  function statusInfo(st) {
    return STATUS[st] || STATUS[String(st)] || { label: String(st), cls: "" };
  }

  function statusName(st) {
    if (typeof st === "string") return st;
    const map = ["Scheduled", "Working", "OnBreak", "Completed", "Absent", "Late", "Cancelled"];
    return map[st] || String(st);
  }

  async function api(path, opts = {}) {
    const headers = { Accept: "application/json", ...(opts.headers || {}) };
    if (opts.body && !headers["Content-Type"]) headers["Content-Type"] = "application/json";
    if (token) headers.Authorization = `Bearer ${token}`;
    const res = await fetch(path, { ...opts, headers });
    let json = null;
    try {
      json = await res.json();
    } catch {
      json = null;
    }
    if (res.status === 401) {
      logout(false);
      throw new Error("Сессия истекла — войдите снова");
    }
    if (!res.ok || (json && json.success === false)) {
      throw new Error((json && (json.message || json.error)) || `Ошибка ${res.status}`);
    }
    return json?.data ?? json;
  }

  function logout(renderNow = true) {
    token = "";
    me = null;
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ME_KEY);
    if (renderNow) render();
  }

  async function login(loginName, password) {
    busy = true;
    error = "";
    render();
    try {
      const data = await api("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ login: loginName, password }),
      });
      token = data.accessToken;
      me = {
        employeeId: data.employee.id,
        login: data.employee.login,
        displayName: data.employee.displayName,
        canManageSchedule: (data.employee.permissions || []).some((p) =>
          ["schedules.manage", "employees.manage"].includes(String(p).toLowerCase())
        ),
        permissions: data.employee.permissions || [],
      };
      localStorage.setItem(TOKEN_KEY, token);
      localStorage.setItem(ME_KEY, JSON.stringify(me));
      flash = `Привет, ${me.displayName}`;
      await refresh();
    } catch (e) {
      error = e.message || "Не удалось войти";
      render();
    } finally {
      busy = false;
    }
  }

  async function refreshMe() {
    const data = await api("/api/work/me");
    me = {
      employeeId: data.employeeId,
      login: data.login,
      displayName: data.displayName,
      canManageSchedule: !!data.canManageSchedule,
      permissions: data.permissions || [],
    };
    localStorage.setItem(ME_KEY, JSON.stringify(me));
  }

  async function refresh() {
    if (!token) {
      render();
      return;
    }
    busy = true;
    error = "";
    render();
    try {
      await refreshMe();
      if (tab === "today") {
        board = await api(`/api/work/board?date=${encodeURIComponent(boardDate)}`);
      } else if (tab === "mine") {
        const from = addDays(todayAlmaty(), -7);
        const to = addDays(todayAlmaty(), 21);
        myShifts = await api(`/api/work/my-shifts?from=${from}&to=${to}`);
      } else if (tab === "admin" && me?.canManageSchedule) {
        employees = await api("/api/work/employees");
        const from = addDays(boardDate, -7);
        const to = addDays(boardDate, 21);
        adminShifts = await api(`/api/work/shifts?from=${from}&to=${to}`);
      }
      render();
    } catch (e) {
      error = e.message || "Ошибка загрузки";
      render();
    } finally {
      busy = false;
      render();
    }
  }

  function addDays(isoDate, n) {
    const d = new Date(`${isoDate}T12:00:00+05:00`);
    d.setDate(d.getDate() + n);
    return new Intl.DateTimeFormat("en-CA", {
      timeZone: "Asia/Almaty",
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
    }).format(d);
  }

  async function act(shiftId, action, body) {
    busy = true;
    error = "";
    flash = "";
    render();
    try {
      await api(`/api/work/shifts/${shiftId}/${action}`, {
        method: "POST",
        body: body ? JSON.stringify(body) : JSON.stringify({ comment: null }),
      });
      flash = "Сохранено";
      await refresh();
    } catch (e) {
      error = e.message || "Ошибка";
      busy = false;
      render();
    }
  }

  async function addNote(shiftId) {
    const text = (noteDrafts[shiftId] || "").trim();
    if (!text) return;
    busy = true;
    error = "";
    try {
      await api(`/api/work/shifts/${shiftId}/notes`, {
        method: "POST",
        body: JSON.stringify({ text, kind: me?.canManageSchedule ? "Admin" : "Attendance" }),
      });
      noteDrafts[shiftId] = "";
      flash = "Заметка добавлена";
      await refresh();
    } catch (e) {
      error = e.message || "Ошибка";
      busy = false;
      render();
    }
  }

  function renderTop() {
    if (!me) {
      topActions.innerHTML = `<a class="btn btn-sm btn-ghost" href="/">На сайт</a>`;
      return;
    }
    topActions.innerHTML = `
      <span class="muted" style="font-size:.9rem">${escapeHtml(me.displayName)}</span>
      <button type="button" class="btn btn-sm btn-ghost" id="btnLogout">Выйти</button>
    `;
    document.getElementById("btnLogout")?.addEventListener("click", () => logout());
  }

  function shiftCard(s, opts = {}) {
    const st = statusName(s.status);
    const info = statusInfo(st);
    const mine = !!s.isMine || opts.forceMine;
    const notes = s.notes || [];
    let actions = "";
    if (mine || me?.canManageSchedule) {
      if (["Scheduled", "Late", "Absent"].includes(st)) {
        actions += `<button type="button" class="btn btn-sm btn-ok" data-act="clock-in" data-id="${s.id}">Пришёл</button>`;
      }
      if (["Working", "Late"].includes(st)) {
        actions += `<button type="button" class="btn btn-sm btn-warn" data-act="break-start" data-id="${s.id}">Перерыв</button>`;
        actions += `<button type="button" class="btn btn-sm" data-act="clock-out" data-id="${s.id}">Ушёл</button>`;
      }
      if (st === "OnBreak") {
        actions += `<button type="button" class="btn btn-sm btn-ok" data-act="break-end" data-id="${s.id}">С перерыва</button>`;
        actions += `<button type="button" class="btn btn-sm" data-act="clock-out" data-id="${s.id}">Ушёл</button>`;
      }
    }
    if (me?.canManageSchedule && st === "Scheduled") {
      actions += `<button type="button" class="btn btn-sm btn-bad" data-act="absent" data-id="${s.id}">Неявка</button>`;
      actions += `<button type="button" class="btn btn-sm btn-ghost" data-admin="cancel" data-id="${s.id}">Отменить</button>`;
    }

    const notesHtml = notes
      .slice(0, 8)
      .map(
        (n) => `
      <div class="note">
        <div class="note__meta">${escapeHtml(n.authorName)} · ${fmtWhen(n.createdAt)} · ${escapeHtml(n.kind || "")}</div>
        <div>${escapeHtml(n.text)}</div>
      </div>`
      )
      .join("");

    return `
      <article class="shift ${mine ? "mine" : ""}" data-shift="${s.id}">
        <div class="shift__head">
          <div>
            <div class="shift__name">${escapeHtml(s.employeeName)}${mine ? " · вы" : ""}</div>
            <div class="shift__meta">
              ${escapeHtml(String(s.workDate))} · план ${fmtPlan(s.plannedStart, s.plannedEnd)}
              ${s.workedHours > 0 ? ` · факт ${s.workedHours} ч` : ""}
              ${s.breakMinutes ? ` · перерыв ${s.breakMinutes} мин` : ""}
            </div>
            <div class="shift__meta">
              приход ${fmtWhen(s.actualStartAt)} · уход ${fmtWhen(s.actualEndAt)}
              ${s.comment ? ` · ${escapeHtml(s.comment)}` : ""}
            </div>
          </div>
          <span class="chip ${info.cls}">${info.label}</span>
        </div>
        ${actions ? `<div class="actions">${actions}</div>` : ""}
        <div class="notes">
          ${notesHtml || `<div class="muted" style="font-size:.9rem">Пометок пока нет</div>`}
          <div class="field" style="margin-top:10px">
            <textarea placeholder="Пометка: опоздание, замена, что на смене…" data-note="${s.id}">${escapeHtml(noteDrafts[s.id] || "")}</textarea>
          </div>
          <button type="button" class="btn btn-sm btn-ghost" data-note-save="${s.id}">Сохранить пометку</button>
        </div>
      </article>`;
  }

  function renderLogin() {
    app.innerHTML = `
      <section class="hero">
        <p class="muted" style="letter-spacing:.18em;font-family:var(--font-display);color:var(--orange);text-transform:uppercase;font-size:.8rem">SHIFT Club</p>
        <h1>Смены</h1>
        <p class="muted">Кто сегодня на работе, график, приход / уход и пометки по смене.</p>
      </section>
      <section class="panel">
        <h2>Вход сотрудника</h2>
        <form class="form-grid" id="loginForm" autocomplete="off" data-lpignore="true" data-1p-ignore="true" data-bwignore="true">
          <div class="field">
            <label>Логин или телефон</label>
            <input
              name="work_login"
              id="workLogin"
              type="text"
              inputmode="text"
              autocomplete="off"
              autocapitalize="off"
              autocorrect="off"
              spellcheck="false"
              data-lpignore="true"
              data-1p-ignore="true"
              data-form-type="other"
              readonly
              required
            />
          </div>
          <div class="field">
            <label>Пароль</label>
            <input
              name="work_password"
              id="workPassword"
              type="password"
              autocomplete="new-password"
              data-lpignore="true"
              data-1p-ignore="true"
              data-form-type="other"
              readonly
              required
            />
          </div>
          ${error ? `<p class="error">${escapeHtml(error)}</p>` : ""}
          <button class="btn" type="submit" ${busy ? "disabled" : ""}>${busy ? "…" : "Войти"}</button>
        </form>
      </section>`;
    const unlock = (el) => el?.removeAttribute("readonly");
    const loginEl = document.getElementById("workLogin");
    const passEl = document.getElementById("workPassword");
    loginEl?.addEventListener("focus", () => unlock(loginEl));
    passEl?.addEventListener("focus", () => unlock(passEl));
    loginEl?.addEventListener("pointerdown", () => unlock(loginEl));
    passEl?.addEventListener("pointerdown", () => unlock(passEl));
    document.getElementById("loginForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      login(String(loginEl?.value || "").trim(), String(passEl?.value || ""));
    });
  }

  function renderApp() {
    const tabs = `
      <div class="tabs">
        <button type="button" class="tab ${tab === "today" ? "is-active" : ""}" data-tab="today">Сегодня</button>
        <button type="button" class="tab ${tab === "mine" ? "is-active" : ""}" data-tab="mine">Мой график</button>
        ${me.canManageSchedule ? `<button type="button" class="tab ${tab === "admin" ? "is-active" : ""}" data-tab="admin">Расписание</button>` : ""}
      </div>`;

    let body = "";
    if (tab === "today") {
      const shifts = board?.shifts || [];
      body = `
        <div class="date-row">
          <input type="date" id="boardDate" value="${boardDate}" />
          <button type="button" class="btn btn-sm btn-ghost" id="btnToday">Сегодня</button>
          <span class="muted">${board?.timeZoneId || "Asia/Almaty"}</span>
        </div>
        ${board?.myShift ? `<h2 style="margin:0 0 8px;font-size:1rem">Ваша смена</h2>${shiftCard(board.myShift, { forceMine: true })}` : `<div class="empty">На эту дату у вас нет смены</div>`}
        <h2 style="margin:18px 0 8px;font-size:1rem">Команда на смене</h2>
        <div class="shift-list">
          ${shifts.length ? shifts.map((s) => shiftCard(s)).join("") : `<div class="empty">Никого в графике</div>`}
        </div>`;
    } else if (tab === "mine") {
      body = `
        <div class="shift-list">
          ${myShifts.length ? myShifts.map((s) => shiftCard(s, { forceMine: true })).join("") : `<div class="empty">Смен в ближайшие дни нет</div>`}
        </div>`;
    } else {
      body = renderAdmin();
    }

    app.innerHTML = `
      <section class="hero">
        <h1>Рабочий день</h1>
        <p class="muted">Отмечайте приход и уход, смотрите кто на смене, оставляйте пометки.</p>
      </section>
      ${tabs}
      ${flash ? `<p class="flash">${escapeHtml(flash)}</p>` : ""}
      ${error ? `<p class="error">${escapeHtml(error)}</p>` : ""}
      <section class="panel">${body}</section>`;

    bindAppEvents();
  }

  function renderAdmin() {
    const empOpts = (employees || [])
      .map((e) => `<option value="${e.id}">${escapeHtml(e.displayName)} (${escapeHtml(e.login)})</option>`)
      .join("");
    const list = (adminShifts || [])
      .filter((s) => statusName(s.status) !== "Cancelled")
      .map((s) => {
        const st = statusName(s.status);
        const info = statusInfo(st);
        return `
          <article class="shift">
            <div class="shift__head">
              <div>
                <div class="shift__name">${escapeHtml(s.employeeName)}</div>
                <div class="shift__meta">${escapeHtml(String(s.workDate))} · ${fmtPlan(s.plannedStart, s.plannedEnd)}
                  · приход ${fmtWhen(s.actualStartAt)} · уход ${fmtWhen(s.actualEndAt)}</div>
              </div>
              <span class="chip ${info.cls}">${info.label}</span>
            </div>
            <div class="actions">
              ${st === "Scheduled" ? `<button type="button" class="btn btn-sm btn-bad" data-admin="absent" data-id="${s.id}">Неявка</button>
              <button type="button" class="btn btn-sm btn-ghost" data-admin="cancel" data-id="${s.id}">Отменить</button>` : ""}
            </div>
          </article>`;
      })
      .join("");

    return `
      <h2>Назначить смену</h2>
      <form class="admin-grid" id="createShiftForm">
        <div class="field span-2">
          <label>Сотрудник</label>
          <select name="employeeId" required>${empOpts}</select>
        </div>
        <div class="field">
          <label>Дата</label>
          <input type="date" name="workDate" value="${boardDate}" required />
        </div>
        <div class="field">
          <label>С</label>
          <input type="time" name="plannedStart" value="10:00" required />
        </div>
        <div class="field">
          <label>До</label>
          <input type="time" name="plannedEnd" value="22:00" required />
        </div>
        <div class="field span-2">
          <label>Комментарий</label>
          <input name="comment" placeholder="опционально" />
        </div>
        <button class="btn" type="submit">Добавить смену</button>
      </form>

      <h2 style="margin-top:22px">Массово на период</h2>
      <form class="admin-grid" id="bulkShiftForm">
        <div class="field span-2">
          <label>Сотрудник</label>
          <select name="employeeId" required>${empOpts}</select>
        </div>
        <div class="field">
          <label>С даты</label>
          <input type="date" name="from" value="${boardDate}" required />
        </div>
        <div class="field">
          <label>По дату</label>
          <input type="date" name="to" value="${addDays(boardDate, 13)}" required />
        </div>
        <div class="field">
          <label>С</label>
          <input type="time" name="plannedStart" value="10:00" required />
        </div>
        <div class="field">
          <label>До</label>
          <input type="time" name="plannedEnd" value="22:00" required />
        </div>
        <div class="field span-2">
          <label>Дни недели</label>
          <div style="display:flex;flex-wrap:wrap;gap:8px;padding-top:4px">
            ${WEEKDAYS.map((d) => `<label style="display:inline-flex;gap:6px;align-items:center;color:var(--muted)"><input type="checkbox" name="wd" value="${d.v}" ${d.v >= 1 && d.v <= 5 ? "checked" : ""}/> ${d.t}</label>`).join("")}
          </div>
        </div>
        <button class="btn btn-ghost" type="submit">Создать график</button>
      </form>

      <h2 style="margin-top:22px">Ближайшие смены</h2>
      <div class="shift-list">${list || `<div class="empty">Пусто</div>`}</div>`;
  }

  function bindAppEvents() {
    document.querySelectorAll("[data-tab]").forEach((el) => {
      el.addEventListener("click", () => {
        tab = el.getAttribute("data-tab");
        refresh();
      });
    });

    document.getElementById("boardDate")?.addEventListener("change", (e) => {
      boardDate = e.target.value || todayAlmaty();
      refresh();
    });
    document.getElementById("btnToday")?.addEventListener("click", () => {
      boardDate = todayAlmaty();
      refresh();
    });

    document.querySelectorAll("[data-act]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const id = btn.getAttribute("data-id");
        const action = btn.getAttribute("data-act");
        act(id, action, { comment: null });
      });
    });

    document.querySelectorAll("[data-note]").forEach((ta) => {
      ta.addEventListener("input", () => {
        noteDrafts[ta.getAttribute("data-note")] = ta.value;
      });
    });
    document.querySelectorAll("[data-note-save]").forEach((btn) => {
      btn.addEventListener("click", () => addNote(btn.getAttribute("data-note-save")));
    });

    document.querySelectorAll("[data-admin]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const id = btn.getAttribute("data-id");
        const kind = btn.getAttribute("data-admin");
        busy = true;
        try {
          if (kind === "cancel") await api(`/api/work/shifts/${id}/cancel`, { method: "POST" });
          if (kind === "absent") await api(`/api/work/shifts/${id}/absent`, { method: "POST", body: JSON.stringify({ comment: "Неявка" }) });
          flash = "Обновлено";
          await refresh();
        } catch (e) {
          error = e.message;
          busy = false;
          render();
        }
      });
    });

    document.getElementById("createShiftForm")?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const fd = new FormData(e.target);
      busy = true;
      error = "";
      try {
        await api("/api/work/shifts", {
          method: "POST",
          body: JSON.stringify({
            employeeId: fd.get("employeeId"),
            workDate: fd.get("workDate"),
            plannedStart: `${fd.get("plannedStart")}:00`,
            plannedEnd: `${fd.get("plannedEnd")}:00`,
            comment: String(fd.get("comment") || "").trim() || null,
          }),
        });
        flash = "Смена создана";
        await refresh();
      } catch (err) {
        error = err.message;
        busy = false;
        render();
      }
    });

    document.getElementById("bulkShiftForm")?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const fd = new FormData(e.target);
      const weekdays = [...e.target.querySelectorAll('input[name="wd"]:checked')].map((x) => Number(x.value));
      busy = true;
      error = "";
      try {
        const created = await api("/api/work/shifts/bulk", {
          method: "POST",
          body: JSON.stringify({
            employeeId: fd.get("employeeId"),
            from: fd.get("from"),
            to: fd.get("to"),
            plannedStart: `${fd.get("plannedStart")}:00`,
            plannedEnd: `${fd.get("plannedEnd")}:00`,
            weekdays,
            comment: null,
          }),
        });
        flash = `Создано смен: ${(created || []).length}`;
        await refresh();
      } catch (err) {
        error = err.message;
        busy = false;
        render();
      }
    });
  }

  function escapeHtml(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function render() {
    renderTop();
    if (!token || !me) renderLogin();
    else renderApp();
  }

  render();
  if (token) refresh();
})();
