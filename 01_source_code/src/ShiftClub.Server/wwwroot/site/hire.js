(() => {
  const TOKEN_KEY = "shift_hiring_token";
  const STATUSES = [
    "анкета заполнена",
    "приглашен на собеседование",
    "собеседование проводится",
    "приглашен на пробную смену",
    "пробная смена",
    "резерв",
    "принят",
    "отказ",
  ];

  const SCORE_CRITERIA = [
    ["communication", "Коммуникабельность"],
    ["speech", "Грамотность речи"],
    ["friendliness", "Доброжелательность"],
    ["responsibility", "Ответственность"],
    ["punctuality", "Пунктуальность"],
    ["honesty", "Честность и открытость"],
    ["stress", "Стрессоустойчивость"],
    ["clients", "Работа с клиентами"],
    ["cash", "Потенциал работы с кассой"],
    ["social", "Знание социальных сетей"],
    ["content", "Умение создавать контент"],
    ["initiative", "Инициативность"],
    ["learning", "Обучаемость"],
    ["team", "Командность"],
    ["motivation", "Мотивация"],
    ["growth", "Желание развиваться"],
    ["scheduleFit", "Соответствие графику"],
    ["overall", "Общее впечатление"],
  ];

  const INTERVIEW_Q = [
    ["qWhyShift", "Почему хотите работать именно в SHIFT?"],
    ["qCash", "Как относитесь к материальной ответственности и кассе?"],
    ["qConflict", "Что сделаете, если гость грубит или спорит о правилах?"],
    ["qNight", "Готовы ли к ночным / выходным сменам? Есть ограничения?"],
    ["qContent", "Можете ли снимать / вести контент для соцсетей клуба?"],
    ["qTeam", "Как ведёте себя в команде, если что-то идёт не по плану?"],
    ["qGrowth", "Куда хотите вырасти через 6–12 месяцев?"],
    ["notesLive", "Живые заметки руководителя"],
  ];

  const FLAG_KEYS = [
    ["strongSpeech", "Сильная речь"],
    ["goodEnergy", "Хорошая энергия"],
    ["cashRisk", "Риск по кассе"],
    ["scheduleIssue", "Проблема с графиком"],
    ["contentReady", "Готовность к контенту"],
    ["needsTraining", "Нужно обучение"],
    ["reserveOnly", "Только в резерв"],
  ];

  const ANSWER_LABELS = {
    fullName: "ФИО", birthDate: "Дата рождения", age: "Возраст", phone: "Телефон",
    whatsapp: "WhatsApp", telegram: "Telegram", email: "Email", district: "Район",
    travelTime: "Время до клуба", maritalStatus: "Семейное положение", hasChildren: "Есть ли дети",
    hasLicense: "Водительские права", hasCar: "Личный автомобиль",
    studying: "Учитесь?", studyWhere: "Где учитесь", studyForm: "Форма обучения",
    studyUntil: "До какого времени учитесь", studyEnd: "Когда заканчиваете",
    educationLevel: "Самое высокое образование",
    workingNow: "Работаете сейчас?", workWhere: "Где работаете", whyLeave: "Почему хотите уйти",
    whyLeftPrev: "Почему ушли с прошлого места", jobsLast5Years: "Мест работы за 5 лет",
    longestJob: "Самое долгое место", longestJobWhy: "Почему самое долгое",
    experienceAreas: "Опыт", pcSkill: "Владение ПК", pcPrograms: "Программы",
    gamesKnow: "Какие игры знаете", gamesPlay: "В какие играете", hasSteam: "Steam",
    hasDiscord: "Discord", instagram: "Instagram", tiktok: "TikTok", threads: "Threads",
    socialTime: "Время в соцсетях", likeFilming: "Любите снимать видео",
    contentLikes: "Что нравится в контенте", schedule: "График", nightShifts: "Ночные",
    weekendReady: "Выходные", holidayReady: "Праздники", hoursPerWeek: "Часов в неделю",
    startWhen: "Когда готовы выйти", currentSalary: "Зарплата сейчас", wantedSalary: "Желаемая зарплата",
    priorities: "Что важнее", taskStyle: "Что нравится в задачах", critiqueAttitude: "Отношение к критике",
    stressReaction: "Реакция на стресс", freeTime: "Когда нет работы", selfTraits: "Считаете себя",
    emptyClubAction: "Пустой клуб", problemNoBoss: "Проблема без руководителя",
    ideaWait: "Идея — ждать или предложить", lastImprovement: "Последнее улучшение",
    goodService: "Хороший сервис", adminMain: "Главное в работе администратора",
    guestHappy: "Как понять что клиент доволен", rudeGuest: "Грубый клиент",
    noisyChild: "Шумный ребёнок", pcFrozen: "Завис компьютер", clubIdeas: "Идеи привлечения",
    promoIdeas: "Акции", reelsIdea: "Reels", bloggers: "Блогеры", inOneYear: "Через год",
    inThreeYears: "Через три года", wantMoreResponsibility: "Больше ответственности",
    wantManagePeople: "Руководить людьми", extraDuties: "Помощь не по обязанностям",
    stayForTournament: "Задержаться ради турнира", workOnDayOff: "Выйти в выходной",
    seenDirt: "Увидели грязь", bossWrong: "Руководитель ошибается", whyHireYou: "Почему взять вас",
    weakSides: "Слабые стороны", commonMistakes: "Частые ошибки", whenWrong: "Когда были неправы",
    bossesSaid: "Что говорили руководители", storiesIdea: "Stories", discountPost: "Пост о скидке",
    sellNight: "Продажа ночного тарифа", trialReady: "Пробная смена", limits: "Ограничения",
    questionsToUs: "Вопросы к нам", compatibleNote: "Отношение к задачам вне инструкций"
  };

  const EXP_OPTS = ["касса","обслуживание клиентов","продажи","общепит","компьютерный клуб","магазин","call-center","соцсети","фото","видео","монтаж","Photoshop","Canva","CapCut","Excel","CRM","другое"];
  const CONTENT_OPTS = ["снимать","монтировать","писать тексты","фотографировать","придумывать идеи","отвечать клиентам"];
  const PRIORITY_OPTS = ["зарплата","коллектив","развитие","стабильность","карьерный рост","интересная работа"];
  const TRAIT_OPTS = ["спокойным человеком","активным","лидером","творческим","организатором"];

  function calcAge(iso) {
    if (!iso) return "";
    const d = new Date(iso + "T00:00:00");
    if (Number.isNaN(d.getTime())) return "";
    const t = new Date();
    let age = t.getFullYear() - d.getFullYear();
    const m = t.getMonth() - d.getMonth();
    if (m < 0 || (m === 0 && t.getDate() < d.getDate())) age--;
    return age >= 0 && age < 120 ? String(age) : "";
  }

  const state = {
    mode: "apply", // apply | manager | detail
    step: 0,
    answers: loadDraft() || {},
    photoFile: null,
    error: "",
    success: "",
    flash: "",
    token: localStorage.getItem(TOKEN_KEY) || "",
    pin: "",
    list: [],
    dash: null,
    filterStatus: "",
    filterQ: "",
    archived: false,
    candidate: null,
    detailTab: "anketa", // anketa | interview | scores | decision
    busy: false,
  };

  const app = document.getElementById("app");
  const btnApply = document.getElementById("btnModeApply");
  const btnMgr = document.getElementById("btnModeMgr");

  btnApply.addEventListener("click", () => {
    state.mode = "apply";
    state.candidate = null;
    state.error = "";
    state.flash = "";
    render();
  });
  btnMgr.addEventListener("click", () => {
    state.mode = "manager";
    state.candidate = null;
    state.error = "";
    state.flash = "";
    render();
    if (state.token) refreshManager();
  });

  function setChrome() {
    const mgr = state.mode === "manager" || state.mode === "detail";
    document.body.classList.toggle("is-apply", state.mode === "apply" && !state.success);
    btnApply.hidden = !mgr;
    btnApply.classList.toggle("is-on", state.mode === "apply");
    btnMgr.classList.toggle("is-on", mgr);
    btnMgr.textContent = mgr ? "Панель" : "Руководитель";
  }

  const steps = [
    { title: "Личная информация", render: stepPersonal },
    { title: "Образование", render: stepEducation },
    { title: "Работа", render: stepWork },
    { title: "Опыт", render: stepExperience },
    { title: "Компьютер", render: stepComputer },
    { title: "Соцсети", render: stepSocial },
    { title: "График", render: stepSchedule },
    { title: "Деньги", render: stepMoney },
    { title: "Личность", render: stepPersonality },
    { title: "Инициативность", render: stepInitiative },
    { title: "Клиенты", render: stepClients },
    { title: "Маркетинг", render: stepMarketing },
    { title: "Рост", render: stepGrowth },
    { title: "Совместимость", render: stepFit },
    { title: "Честность", render: stepHonesty },
    { title: "Практика", render: stepPractice },
    { title: "Финал", render: stepFinal },
  ];

  function loadDraft() {
    try {
      return JSON.parse(localStorage.getItem("shift_hire_draft") || "null");
    } catch {
      return null;
    }
  }
  function saveDraft() {
    localStorage.setItem("shift_hire_draft", JSON.stringify(state.answers));
  }

  async function api(path, opts = {}) {
    const headers = Object.assign({}, opts.headers || {});
    if (state.token) headers["Authorization"] = "Bearer " + state.token;
    if (opts.body && !(opts.body instanceof FormData) && !headers["Content-Type"]) {
      headers["Content-Type"] = "application/json";
    }
    const res = await fetch("/api/hiring" + path, { ...opts, headers });
    const json = await res.json().catch(() => ({}));
    if (!res.ok || json.success === false) {
      const msg = (json && json.message) || "Ошибка запроса";
      if (res.status === 401) {
        state.token = "";
        localStorage.removeItem(TOKEN_KEY);
      }
      throw new Error(msg);
    }
    return json.data;
  }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function val(key, fallback = "") {
    const v = state.answers[key];
    return v == null ? fallback : v;
  }

  function setAns(key, value) {
    state.answers[key] = value;
    saveDraft();
  }

  function fieldText(key, label, opts = {}) {
    const type = opts.type || "text";
    const ph = opts.placeholder || "";
    return `<div class="field"><label for="${key}">${esc(label)}</label>
      <input id="${key}" name="${key}" type="${type}" value="${esc(val(key))}" placeholder="${esc(ph)}" ${opts.required ? "required" : ""} /></div>`;
  }
  function fieldArea(key, label, ph = "") {
    return `<div class="field"><label for="${key}">${esc(label)}</label>
      <textarea id="${key}" name="${key}" placeholder="${esc(ph)}">${esc(val(key))}</textarea></div>`;
  }
  function fieldSelect(key, label, options) {
    const opts = options
      .map((o) => `<option value="${esc(o)}" ${val(key) === o ? "selected" : ""}>${esc(o)}</option>`)
      .join("");
    return `<div class="field"><label for="${key}">${esc(label)}</label>
      <select id="${key}" name="${key}"><option value="">—</option>${opts}</select></div>`;
  }
  function fieldChecks(key, label, options) {
    const cur = Array.isArray(state.answers[key]) ? state.answers[key] : [];
    const items = options
      .map(
        (o) => `<label class="check"><input type="checkbox" data-arr="${key}" value="${esc(o)}" ${
          cur.includes(o) ? "checked" : ""
        } />${esc(o)}</label>`
      )
      .join("");
    return `<div class="field"><label>${esc(label)}</label><div class="checks">${items}</div></div>`;
  }

  function stepPersonal() {
    const age = calcAge(val("birthDate")) || val("age") || "";
    return `
      ${fieldText("fullName", "ФИО *", { required: true, placeholder: "Имя Фамилия" })}
      ${fieldText("birthDate", "Дата рождения", { type: "date" })}
      <div class="field"><label>Возраст (автоматически)</label>
        <input type="text" id="age" name="age" value="${esc(age)}" readonly /></div>
      ${fieldText("phone", "Телефон *", { required: true, placeholder: "+7 7xx xxx xx xx" })}
      ${fieldText("whatsapp", "WhatsApp", { placeholder: "+7 …" })}
      ${fieldText("telegram", "Telegram", { placeholder: "@username" })}
      ${fieldText("email", "Email", { type: "email" })}
      ${fieldText("district", "Район проживания", { placeholder: "Алматы, район…" })}
      ${fieldSelect("travelTime", "Время до клуба", ["до 20 мин", "20–40 мин", "40–60 мин", "больше часа"])}
      <div class="field"><label for="photo">Фото (по желанию)</label>
        <input id="photo" name="photo" type="file" accept="image/*" /></div>
      ${fieldSelect("maritalStatus", "Семейное положение", ["не женат / не замужем", "в отношениях", "женат / замужем", "другое"])}
      ${fieldSelect("hasChildren", "Есть ли дети", ["нет", "да"])}
      ${fieldSelect("hasLicense", "Есть ли водительские права", ["нет", "да"])}
      ${fieldSelect("hasCar", "Есть ли личный автомобиль", ["нет", "да"])}`;
  }
  function stepEducation() {
    return `
      ${fieldSelect("studying", "Учитесь?", ["нет", "да"])}
      ${fieldText("studyWhere", "Где?")}
      ${fieldSelect("studyForm", "Форма обучения", ["очно", "заочно", "дистанционно", "не учусь"])}
      ${fieldText("studyUntil", "До какого времени учитесь", { placeholder: "например до 17:00 / вечером свободно" })}
      ${fieldText("studyEnd", "Когда заканчиваете", { placeholder: "год / месяц" })}
      ${fieldSelect("educationLevel", "Самое высокое образование", ["школа", "колледж", "бакалавр", "магистр", "другое"])}`;
  }
  function stepWork() {
    return `
      ${fieldSelect("workingNow", "Работаете сейчас?", ["нет", "да"])}
      ${fieldText("workWhere", "Где?")}
      ${fieldArea("whyLeave", "Почему хотите уйти?")}
      ${fieldArea("whyLeftPrev", "Почему ушли с прошлого места?")}
      ${fieldText("jobsLast5Years", "Сколько всего мест работы было за последние 5 лет?", { type: "number" })}
      ${fieldText("longestJob", "Какое место работы было самым долгим?")}
      ${fieldArea("longestJobWhy", "Почему?")}`;
  }
  function stepExperience() {
    return `${fieldChecks("experienceAreas", "Где есть опыт?", EXP_OPTS)}`;
  }
  function stepComputer() {
    return `
      <div class="field"><label for="pcSkill">Насколько хорошо владеете ПК? (1–10)</label>
        <input id="pcSkill" name="pcSkill" type="range" min="1" max="10" step="1" value="${esc(val("pcSkill") || "5")}" />
        <div class="muted" id="pcSkillVal">${esc(val("pcSkill") || "5")}/10</div></div>
      ${fieldArea("pcPrograms", "Какими программами пользуетесь?")}
      ${fieldArea("gamesKnow", "Какие игры знаете?")}
      ${fieldArea("gamesPlay", "Какие игры сами играете?")}
      ${fieldSelect("hasSteam", "Steam есть?", ["да", "нет"])}
      ${fieldSelect("hasDiscord", "Discord?", ["да", "нет"])}`;
  }
  function stepSocial() {
    return `
      ${fieldText("instagram", "Instagram")}
      ${fieldText("tiktok", "TikTok")}
      ${fieldText("threads", "Threads")}
      ${fieldText("telegram", "Telegram")}
      ${fieldSelect("socialTime", "Сколько времени проводите в соцсетях ежедневно?", ["до 1 часа", "1–2 часа", "2–4 часа", "больше 4 часов"])}
      ${fieldSelect("likeFilming", "Любите ли снимать видео?", ["нет", "иногда", "да", "очень"])}
      ${fieldChecks("contentLikes", "Что больше нравится:", CONTENT_OPTS)}`;
  }
  function stepSchedule() {
    return `
      ${fieldSelect("schedule", "Какой график готовы работать?", ["утро / день", "вечер", "ночь", "гибкий", "любой", "2/2", "5/2"])}
      ${fieldSelect("nightShifts", "Ночные?", ["да", "иногда", "нет"])}
      ${fieldSelect("weekendReady", "Выходные?", ["да", "иногда", "нет"])}
      ${fieldSelect("holidayReady", "Праздники?", ["да", "иногда", "нет"])}
      ${fieldSelect("hoursPerWeek", "Сколько часов?", ["до 20", "20–30", "30–40", "полный график", "сколько нужно"])}
      ${fieldText("startWhen", "Через сколько готовы выйти?", { placeholder: "сразу / через неделю…" })}`;
  }
  function stepMoney() {
    return `
      ${fieldText("currentSalary", "Какая зарплата сейчас? (₸)", { placeholder: "если нет — 0" })}
      ${fieldText("wantedSalary", "Какую хотите? (₸)", { placeholder: "например 180000" })}
      ${fieldChecks("priorities", "Что для вас важнее?", PRIORITY_OPTS)}`;
  }
  function stepPersonality() {
    return `
      ${fieldSelect("taskStyle", "Что вам больше нравится?", ["выполнять готовые задачи", "самому придумывать решения"])}
      ${fieldArea("critiqueAttitude", "Как вы относитесь к критике?")}
      ${fieldArea("stressReaction", "Как реагируете на стресс?")}
      ${fieldArea("freeTime", "Что обычно делаете, когда нет работы?")}
      ${fieldChecks("selfTraits", "Вы считаете себя:", TRAIT_OPTS)}`;
  }
  function stepInitiative() {
    return `
      <p class="help">Самый важный раздел. Отвечайте честно и конкретно.</p>
      ${fieldArea("emptyClubAction", "Клуб пустой. Что будете делать?")}
      ${fieldArea("problemNoBoss", "Вы увидели проблему. Руководителя нет. Что будете делать?")}
      ${fieldSelect("ideaWait", "Вы придумали хорошую идею. Будете ждать пока спросят или предложите?", ["буду ждать", "предложу сам"])}
      ${fieldArea("lastImprovement", "Когда в последний раз вы сами предлагали улучшение на работе? Опишите.")}`;
  }
  function stepClients() {
    return `
      ${fieldArea("goodService", "Что такое хороший сервис?")}
      ${fieldArea("adminMain", "Что самое главное в работе администратора?")}
      ${fieldArea("guestHappy", "Как понять, что клиент доволен?")}
      ${fieldArea("rudeGuest", "Если клиент начал грубить?")}
      ${fieldArea("noisyChild", "Если ребенок шумит?")}
      ${fieldArea("pcFrozen", "Если компьютер завис?")}`;
  }
  function stepMarketing() {
    return `
      ${fieldArea("clubIdeas", "Как привлечь людей в компьютерный клуб? Минимум 3 идеи.")}
      ${fieldArea("promoIdeas", "Какие акции сделали бы?")}
      ${fieldArea("reelsIdea", "Какой Reels сейчас может залететь?")}
      ${fieldArea("bloggers", "Какие блогеры подходят клубу?")}`;
  }
  function stepGrowth() {
    return `
      ${fieldArea("inOneYear", "Кем хотите быть через год?")}
      ${fieldArea("inThreeYears", "Через три года?")}
      ${fieldSelect("wantMoreResponsibility", "Готовы ли брать больше ответственности?", ["да", "пока нет", "не уверен"])}
      ${fieldSelect("wantManagePeople", "Хотите ли руководить людьми?", ["да", "когда-нибудь", "нет"])}`;
  }
  function stepFit() {
    return `
      <p class="help">Мы строим клуб практически с нуля. Иногда нужно помогать не только в своих обязанностях.</p>
      ${fieldSelect("extraDuties", "Как вы к этому относитесь?", ["с радостью", "нормально", "только свои обязанности", "нет"])}
      ${fieldSelect("stayForTournament", "Если нужно задержаться ради турнира?", ["да", "иногда", "нет"])}
      ${fieldSelect("workOnDayOff", "Если понадобится выйти в свой выходной?", ["да", "если важно", "нет"])}
      ${fieldSelect("seenDirt", "Если увидели грязь?", ["уберу сам", "скажу коллеге", "подожду уборку"])}
      ${fieldSelect("bossWrong", "Если заметили, что руководитель ошибается?", ["мягко скажу", "скажу наедине", "промолчу", "другое"])}
      ${fieldArea("compatibleNote", "Коротко своими словами — как относитесь к задачам «не по инструкции»?")}`;
  }
  function stepHonesty() {
    return `
      ${fieldArea("whyHireYou", "Почему именно вас мы должны взять?")}
      ${fieldArea("weakSides", "Какие ваши слабые стороны?")}
      ${fieldArea("commonMistakes", "Какие ошибки вы чаще всего допускаете?")}
      ${fieldArea("whenWrong", "Расскажите случай, когда вы были неправы.")}
      ${fieldArea("bossesSaid", "Что вам чаще всего говорили предыдущие руководители?")}`;
  }
  function stepPractice() {
    return `
      ${fieldArea("storiesIdea", "Придумайте Stories для клуба.")}
      ${fieldArea("discountPost", "Напишите пост о скидке.")}
      ${fieldArea("sellNight", "Продайте человеку ночной тариф (как бы сказали гостю).")}`;
  }
  function stepFinal() {
    const consent = state.answers.consent === true;
    return `
      ${fieldText("startWhen", "Когда готовы выйти?", { placeholder: "дата или «сразу»" })}
      ${fieldSelect("trialReady", "Готовы к пробной смене?", ["да", "нужно уточнить дату", "нет"])}
      ${fieldArea("limits", "Есть ли ограничения?")}
      ${fieldArea("questionsToUs", "Есть ли вопросы к нам?")}
      <label class="check"><input type="checkbox" id="consent" ${consent ? "checked" : ""} />
        Согласен(на) на обработку персональных данных для подбора персонала SHIFT Cyber Club *</label>`;
  }

  function bindFormFields(root) {
    root.querySelectorAll("input, select, textarea").forEach((el) => {
      if (el.id === "photo") {
        el.addEventListener("change", () => {
          state.photoFile = el.files && el.files[0] ? el.files[0] : null;
        });
        return;
      }
      if (el.id === "consent") {
        el.addEventListener("change", () => {
          setAns("consent", el.checked);
          render();
        });
        return;
      }
      if (el.id === "age") return;
      if (el.dataset.arr) {
        el.addEventListener("change", () => {
          const key = el.dataset.arr;
          const boxes = [...root.querySelectorAll(`input[data-arr="${key}"]`)];
          setAns(
            key,
            boxes.filter((b) => b.checked).map((b) => b.value)
          );
        });
        return;
      }
      const key = el.name || el.id;
      if (!key) return;
      const handler = () => {
        setAns(key, el.type === "checkbox" ? el.checked : el.value);
        if (key === "birthDate") {
          const age = calcAge(el.value);
          setAns("age", age);
          const ageEl = root.querySelector("#age");
          if (ageEl) ageEl.value = age;
        }
        if (key === "pcSkill") {
          const lab = root.querySelector("#pcSkillVal");
          if (lab) lab.textContent = el.value + "/10";
        }
      };
      el.addEventListener("input", handler);
      el.addEventListener("change", handler);
    });
  }

  function validateStep() {
    if (state.step === 0) {
      if (!String(val("fullName")).trim()) return "Укажите ФИО";
      if (String(val("phone")).replace(/\D/g, "").length < 10) return "Укажите телефон";
    }
    if (state.step === steps.length - 1 && state.answers.consent !== true)
      return "Нужно согласие на обработку данных";
    return "";
  }

  async function submitApply() {
    const err = validateStep();
    if (err) {
      state.error = err;
      render();
      return;
    }
    state.busy = true;
    state.error = "";
    render();
    try {
      const fd = new FormData();
      fd.append("answersJson", JSON.stringify(state.answers));
      if (state.photoFile) fd.append("photo", state.photoFile);
      const data = await api("/apply", { method: "POST", body: fd });
      localStorage.removeItem("shift_hire_draft");
      state.answers = {};
      state.photoFile = null;
      state.success = data.message || "Анкета отправлена";
      state.step = 0;
    } catch (e) {
      state.error = e.message;
    } finally {
      state.busy = false;
      render();
    }
  }

  function renderApply() {
    setChrome();
    if (state.success) {
      document.body.classList.remove("is-apply");
      app.innerHTML = `
        <div class="hero-mini">
          <p class="eyebrow">SHIFT Cyber Club</p>
          <h1>Готово</h1>
          <p class="lead">${esc(state.success)}</p>
          <p class="muted" style="margin-top:12px">Алматы · Масанчи 86а</p>
          <div class="btn-row" style="max-width:360px">
            <a class="btn" href="/">На сайт</a>
            <button type="button" class="btn btn-ghost" id="again">Ещё анкета</button>
          </div>
        </div>`;
      document.getElementById("again").onclick = () => {
        state.success = "";
        render();
      };
      return;
    }

    const s = steps[state.step];
    const bars = steps.map((_, i) => `<span class="${i <= state.step ? "on" : ""}"></span>`).join("");
    const nextLabel =
      state.step === steps.length - 1 ? (state.busy ? "Отправка…" : "Отправить") : "Далее";
    app.innerHTML = `
      <div class="hero-mini">
        <p class="eyebrow">Вакансия · команда</p>
        <h1>Анкета кандидата</h1>
        <p class="lead">SHIFT Cyber Club · 17 шагов. Отвечайте честно — дальше личное собеседование.</p>
      </div>
      <div class="progress">${bars}</div>
      <p class="step-label">Шаг ${state.step + 1} из ${steps.length}: ${esc(s.title)}</p>
      ${state.error ? `<div class="err">${esc(state.error)}</div>` : ""}
      <form id="hireForm">${s.render()}</form>
      <div class="sticky-bar"><div class="inner">
        <button type="button" class="btn btn-ghost" id="prev" ${state.step === 0 ? "disabled" : ""}>Назад</button>
        <button type="button" class="btn" id="next" ${state.busy ? "disabled" : ""}>${nextLabel}</button>
      </div></div>`;

    bindFormFields(document.getElementById("hireForm"));
    const goPrev = () => {
      state.error = "";
      state.step = Math.max(0, state.step - 1);
      render();
      window.scrollTo(0, 0);
    };
    const goNext = () => {
      const e = validateStep();
      if (e) {
        state.error = e;
        render();
        return;
      }
      state.error = "";
      if (state.step >= steps.length - 1) submitApply();
      else {
        state.step++;
        render();
        window.scrollTo(0, 0);
      }
    };
    document.getElementById("prev").onclick = goPrev;
    document.getElementById("next").onclick = goNext;
  }

  async function refreshManager() {
    try {
      state.dash = await api("/dashboard");
      const q = new URLSearchParams();
      if (state.filterStatus) q.set("status", state.filterStatus);
      if (state.filterQ) q.set("q", state.filterQ);
      if (state.archived) q.set("archived", "true");
      state.list = await api("/candidates?" + q.toString());
      state.error = "";
    } catch (e) {
      state.error = e.message;
      if (!state.token) state.list = [];
    }
    render();
  }

  async function loginMgr() {
    state.busy = true;
    state.error = "";
    render();
    try {
      const data = await api("/login", {
        method: "POST",
        body: JSON.stringify({ pin: state.pin }),
      });
      state.token = data.token;
      localStorage.setItem(TOKEN_KEY, state.token);
      state.pin = "";
      await refreshManager();
    } catch (e) {
      state.error = e.message;
      state.busy = false;
      render();
    } finally {
      state.busy = false;
    }
  }

  function renderLogin() {
    setChrome();
    app.innerHTML = `
      <div class="hero-mini">
        <p class="eyebrow">Служебный вход</p>
        <h1>Панель руководителя</h1>
        <p class="lead">Здесь анкеты кандидатов, собеседование, оценки и решение о найме. Кандидаты эту страницу не видят — нужен PIN.</p>
      </div>
      ${state.error ? `<div class="err">${esc(state.error)}</div>` : ""}
      <div class="field"><label for="pin">PIN руководителя</label>
        <input id="pin" type="password" inputmode="numeric" autocomplete="one-time-code" placeholder="••••" value="${esc(state.pin)}" /></div>
      <button type="button" class="btn btn-block" id="loginBtn" ${state.busy ? "disabled" : ""}>Войти в панель</button>
      <p class="help" style="margin-top:14px">Вернуться к публичной анкете — кнопка «Анкета» сверху.</p>`;
    const pin = document.getElementById("pin");
    pin.addEventListener("input", () => {
      state.pin = pin.value;
    });
    pin.addEventListener("keydown", (e) => {
      if (e.key === "Enter") loginMgr();
    });
    document.getElementById("loginBtn").onclick = loginMgr;
  }

  function renderManagerList() {
    setChrome();
    const d = state.dash || {};
    const statusOpts = [`<option value="">Все статусы</option>`]
      .concat(
        STATUSES.map(
          (s) =>
            `<option value="${esc(s)}" ${state.filterStatus === s ? "selected" : ""}>${esc(s)}</option>`
        )
      )
      .join("");
    const cards = (state.list || [])
      .map((c) => {
        const score =
          c.totalScore != null ? `<span class="badge">${esc(c.totalScore)}%</span>` : "";
        return `<article class="card list-item" data-id="${c.id}" tabindex="0" role="button">
          <div class="list-item__top">
            <h3>${esc(c.fullName)}</h3>
            <span class="list-item__open">Открыть →</span>
          </div>
          <div class="meta">
            <span class="badge">${esc(c.status)}</span>
            ${score}
            <span>${esc(c.phone || "")}</span>
            <span>${esc(c.district || "—")}</span>
            ${c.age != null ? `<span>${c.age} лет</span>` : ""}
          </div>
        </article>`;
      })
      .join("") || `<div class="card"><p class="muted" style="margin:0">Пока нет анкет${state.archived ? " в архиве" : ""}.</p></div>`;

    app.innerHTML = `
      <div class="hero-mini">
        <p class="eyebrow">Панель руководителя</p>
        <h1>Кандидаты</h1>
        <p class="lead">Нажмите на человека, чтобы посмотреть анкету, провести собеседование и выставить оценки.</p>
      </div>
      <div class="dash">
        <div><strong>${d.newForms || 0}</strong><span>новые анкеты</span></div>
        <div><strong>${d.interview || 0}</strong><span>на собеседовании</span></div>
        <div><strong>${d.trial || 0}</strong><span>пробная смена</span></div>
        <div><strong>${d.hired || 0}</strong><span>приняты</span></div>
      </div>
      ${state.flash ? `<div class="ok-banner">${esc(state.flash)}</div>` : ""}
      ${state.error ? `<div class="err">${esc(state.error)}</div>` : ""}
      <div class="toolbar">
        <input id="q" placeholder="Поиск: ФИО или телефон" value="${esc(state.filterQ)}" />
        <select id="st" aria-label="Статус">${statusOpts}</select>
        <label class="check"><input type="checkbox" id="arch" ${state.archived ? "checked" : ""} />Показать архив</label>
        <button type="button" class="btn btn-sm btn-ghost" id="logout">Выйти</button>
      </div>
      <div id="list">${cards}</div>`;

    document.getElementById("q").addEventListener("change", (e) => {
      state.filterQ = e.target.value;
      refreshManager();
    });
    document.getElementById("st").addEventListener("change", (e) => {
      state.filterStatus = e.target.value;
      refreshManager();
    });
    document.getElementById("arch").addEventListener("change", (e) => {
      state.archived = e.target.checked;
      refreshManager();
    });
    document.getElementById("logout").onclick = () => {
      state.token = "";
      localStorage.removeItem(TOKEN_KEY);
      state.flash = "";
      render();
    };
    app.querySelectorAll("[data-id]").forEach((el) => {
      const open = () => openCandidate(+el.dataset.id);
      el.onclick = open;
      el.onkeydown = (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          open();
        }
      };
    });
  }

  async function openCandidate(id) {
    state.busy = true;
    state.flash = "";
    try {
      state.candidate = await api("/candidates/" + id);
      state.mode = "detail";
      state.detailTab = "anketa";
      state.error = "";
    } catch (e) {
      state.error = e.message;
    } finally {
      state.busy = false;
      render();
    }
  }

  function photoUrl(path) {
    if (!path) return "";
    const name = String(path).split("/").pop();
    return "/api/hiring/photos/" + encodeURIComponent(name);
  }

  function answerRows(answers) {
    if (!answers || typeof answers !== "object") return "";
    const skip = new Set(["consent"]);
    const keys = Object.keys(answers).filter((k) => !skip.has(k) && answers[k] !== "" && answers[k] != null);
    if (!keys.length) return `<p class="muted">Пусто</p>`;
    return keys
      .map((k) => {
        let v = answers[k];
        if (Array.isArray(v)) v = v.join(", ");
        if (typeof v === "boolean") v = v ? "да" : "нет";
        const label = ANSWER_LABELS[k] || k;
        return `<div><dt>${esc(label)}</dt><dd>${esc(v)}</dd></div>`;
      })
      .join("");
  }

  function renderDetail() {
    const c = state.candidate;
    if (!c) {
      state.mode = "manager";
      render();
      return;
    }
    setChrome();
    const scores = c.scores || {};
    const interview = c.interview || {};
    const flags = c.flags || {};
    const trial = c.trial || {};
    const decision = c.decision || {};
    const statusOpts = STATUSES.map(
      (s) => `<option value="${esc(s)}" ${c.status === s ? "selected" : ""}>${esc(s)}</option>`
    ).join("");
    const tab = state.detailTab;
    const tabs = [
      ["anketa", "1. Анкета"],
      ["interview", "2. Собеседование"],
      ["scores", "3. Оценки"],
      ["decision", "4. Решение"],
    ]
      .map(
        ([id, label]) =>
          `<button type="button" class="tab ${tab === id ? "is-on" : ""}" data-tab="${id}">${label}</button>`
      )
      .join("");

    const tone = (c.scoreBreakdown && c.scoreBreakdown.tone) || "muted";
    const label = (c.scoreBreakdown && c.scoreBreakdown.label) || "";
    const hints = (c.hints || []).map((h) => `<li>${esc(h)}</li>`).join("");

    let panel = "";
    if (tab === "anketa") {
      const ap = c.autoProfile || {};
      const apItems = (ap.items || [])
        .map(
          (it) => `<div class="profile-row tone-${esc(it.tone || "muted")}">
            <span>${esc(it.emoji || "")} ${esc(it.name)}</span>
            <b>${esc(it.percent)}%</b>
            <i style="width:${Math.min(100, Number(it.percent) || 0)}%"></i>
          </div>`
        )
        .join("");
      panel = `
        <p class="help">Кандидат этот блок не видит. Предварительный профиль считается автоматически по ответам анкеты.</p>
        <div class="card">
          <h3>Автопрофиль</h3>
          <p class="muted" style="margin:0 0 10px">${esc(ap.summary || "—")} · итог ${esc(ap.fitPercent ?? "—")}%</p>
          <div class="profile-list">${apItems || "<p class=\"muted\">Нет данных</p>"}</div>
        </div>
        <div class="card"><h3>На что обратить внимание</h3><ul class="hints">${hints || "<li>Смотрите автопрофиль и практику</li>"}</ul></div>
        <div class="card"><h3>Ответы анкеты</h3><div class="answers"><dl>${answerRows(c.answers)}</dl></div></div>`;
    } else if (tab === "interview") {
      const iq = INTERVIEW_Q.map(
        ([k, q]) =>
          `<div class="field"><label>${esc(q)}</label><textarea data-iv="${k}" rows="3">${esc(interview[k] || "")}</textarea></div>`
      ).join("");
      panel = `
        <p class="help">Задавайте вопросы по скрипту и сразу пишите ответы / впечатления.</p>
        <div class="card"><h3>Скрипт собеседования</h3>${iq}</div>`;
    } else if (tab === "scores") {
      const scoreRows = SCORE_CRITERIA.map(([code, name]) => {
        const v = scores[code] != null ? scores[code] : 5;
        return `<div class="score-row">
          <div class="score-row__top"><span>${esc(name)}</span><b id="sv_${code}">${v}</b>/10</div>
          <input type="range" min="0" max="10" step="1" data-score="${code}" value="${v}" />
        </div>`;
      }).join("");
      const flagHtml = FLAG_KEYS.map(
        ([k, name]) =>
          `<label class="check"><input type="checkbox" data-flag="${k}" ${flags[k] ? "checked" : ""} />${esc(name)}</label>`
      ).join("");
      panel = `
        <p class="help">Поставьте оценки от 0 до 10. Итоговый % и рекомендация появятся после сохранения.</p>
        <div class="card"><h3>Оценки</h3><div class="score-grid">${scoreRows}</div></div>
        <div class="card"><h3>Метки</h3><div class="checks">${flagHtml}</div></div>`;
    } else {
      panel = `
        <p class="help">Пробная смена и финальное решение по кандидату.</p>
        <div class="card"><h3>Пробная смена</h3>
          ${fieldish("trialDate", "Дата пробной", trial.date || "", "date")}
          ${fieldish("trialResult", "Как прошла", trial.result || "")}
          ${fieldish("trialNotes", "Заметки по смене", trial.notes || "", "area")}
        </div>
        <div class="card"><h3>Итог</h3>
          ${fieldish("decSummary", "Решение / комментарий", decision.summary || "", "area")}
          <div class="field"><label>Личные заметки руководителя</label>
            <textarea id="mgrNotes">${esc(c.managerNotes || "")}</textarea></div>
        </div>`;
    }

    app.innerHTML = `
      <button type="button" class="linkish" id="backList">← Все кандидаты</button>
      <div class="cand-head">
        ${c.photoPath ? `<img class="photo" src="${esc(photoUrl(c.photoPath))}" alt="" />` : ""}
        <div>
          <p class="eyebrow">Кандидат №${c.id}</p>
          <h1 style="font-size:1.45rem">${esc(c.fullName)}</h1>
          <div class="meta" style="margin-top:8px">
            <span>${esc(c.phone || "")}</span>
            <span>${esc(c.messenger || "")}</span>
            <span>${esc(c.district || "")}</span>
            ${c.age != null ? `<span>${c.age} лет</span>` : ""}
          </div>
          ${
            c.totalScore != null
              ? `<p style="margin:8px 0 0"><span class="badge tone-${esc(tone)}">${esc(c.totalScore)}% · ${esc(label)}</span></p>`
              : `<p class="muted" style="margin:8px 0 0;font-size:0.85rem">Оценка появится после вкладки «Оценки»</p>`
          }
        </div>
      </div>
      ${state.error ? `<div class="err">${esc(state.error)}</div>` : ""}
      ${state.flash ? `<div class="ok-banner">${esc(state.flash)}</div>` : ""}
      <div class="field"><label>Текущий статус</label><select id="status">${statusOpts}</select></div>
      <div class="tabs">${tabs}</div>
      <div id="tabPanel">${panel}</div>
      <div class="detail-actions">
        <button type="button" class="btn" id="saveCand">Сохранить</button>
        <button type="button" class="btn btn-ghost" id="archCand">В архив</button>
      </div>`;

    app.querySelectorAll("[data-tab]").forEach((el) => {
      el.onclick = () => {
        // preserve unsaved edits in memory before switching tabs
        stashDetailDraft();
        state.detailTab = el.dataset.tab;
        state.flash = "";
        render();
      };
    });
    app.querySelectorAll("input[data-score]").forEach((el) => {
      el.addEventListener("input", () => {
        document.getElementById("sv_" + el.dataset.score).textContent = el.value;
      });
    });
    document.getElementById("backList").onclick = () => {
      state.mode = "manager";
      state.candidate = null;
      state.flash = "";
      refreshManager();
    };
    document.getElementById("saveCand").onclick = () => saveCandidate();
    document.getElementById("archCand").onclick = async () => {
      if (!confirm("Убрать кандидата в архив?")) return;
      await api("/candidates/" + c.id + "/archive", {
        method: "POST",
        body: JSON.stringify({ archived: true }),
      });
      state.mode = "manager";
      state.candidate = null;
      state.flash = "Кандидат в архиве";
      refreshManager();
    };
  }

  function stashDetailDraft() {
    const c = state.candidate;
    if (!c) return;
    if (!c.scores) c.scores = {};
    if (!c.interview) c.interview = {};
    if (!c.flags) c.flags = {};
    if (!c.trial) c.trial = {};
    if (!c.decision) c.decision = {};
    app.querySelectorAll("input[data-score]").forEach((el) => {
      c.scores[el.dataset.score] = Number(el.value);
    });
    app.querySelectorAll("[data-iv]").forEach((el) => {
      c.interview[el.dataset.iv] = el.value;
    });
    app.querySelectorAll("[data-flag]").forEach((el) => {
      c.flags[el.dataset.flag] = el.checked;
    });
    const st = document.getElementById("status");
    if (st) c.status = st.value;
    const td = document.getElementById("trialDate");
    if (td) c.trial.date = td.value;
    const tr = document.getElementById("trialResult");
    if (tr) c.trial.result = tr.value;
    const tn = document.getElementById("trialNotes");
    if (tn) c.trial.notes = tn.value;
    const ds = document.getElementById("decSummary");
    if (ds) c.decision.summary = ds.value;
    const mn = document.getElementById("mgrNotes");
    if (mn) c.managerNotes = mn.value;
  }

  function fieldish(id, label, value, kind) {
    if (kind === "area")
      return `<div class="field"><label>${esc(label)}</label><textarea id="${id}">${esc(value)}</textarea></div>`;
    const type = kind === "date" ? "date" : "text";
    return `<div class="field"><label>${esc(label)}</label><input id="${id}" type="${type}" value="${esc(value)}" /></div>`;
  }

  async function saveCandidate() {
    const c = state.candidate;
    stashDetailDraft();
    const btn = document.getElementById("saveCand");
    if (btn) {
      btn.disabled = true;
      btn.textContent = "Сохранение…";
    }
    state.error = "";
    try {
      state.candidate = await api("/candidates/" + c.id, {
        method: "PATCH",
        body: JSON.stringify({
          status: c.status,
          scores: c.scores || {},
          interview: c.interview || {},
          flags: c.flags || {},
          trial: c.trial || {},
          decision: c.decision || {},
          managerNotes: c.managerNotes || "",
        }),
      });
      state.flash = "Сохранено";
      state.mode = "detail";
      render();
    } catch (e) {
      state.error = e.message;
      render();
    }
  }

  function render() {
    if (state.mode === "apply") return renderApply();
    if (state.mode === "detail") return renderDetail();
    if (!state.token) return renderLogin();
    return renderManagerList();
  }

  // Deep link manager
  if (location.hash === "#manager" || new URLSearchParams(location.search).has("manager")) {
    state.mode = "manager";
  }

  render();
  if (state.mode === "manager" && state.token) refreshManager();
})();
