const PAGES = [
  { group: "Старт", id: "index", href: "index.html", title: "С чего начать", keys: "обзор первый день чеклист возможности" },
  { group: "Старт", id: "install", href: "install.html", title: "Установка сервера", keys: "мастер postgres порт брандмауэр папка" },
  { group: "Старт", id: "uninstall", href: "uninstall.html", title: "Удаление сервера", keys: "uninstall удалитель база" },
  { group: "Старт", id: "branding", href: "branding.html", title: "Название и логотип", keys: "бренд клуб логотип кэш" },
  { group: "Старт", id: "license", href: "license.html", title: "Лицензия", keys: "ключ места срок shift" },
  { group: "Старт", id: "client", href: "client.html", title: "Клиент на компьютеры", keys: "shell установщик ccboot watchdog" },
  { group: "Панель", id: "panel", href: "panel.html", title: "Панель и права", keys: "вход owner кассир роли" },
  { group: "Панель", id: "hall", href: "hall.html", title: "Зал, зоны, тарифы, сеансы", keys: "карта пк wol тариф пакет ночь" },
  { group: "Панель", id: "cash", href: "cash.html", title: "Касса", keys: "смена чек kaspi z-отчёт возврат" },
  { group: "Панель", id: "bar", href: "bar.html", title: "Бар и склад", keys: "товар остаток заказ pos" },
  { group: "Панель", id: "guests", href: "guests.html", title: "Клиенты, брони, кейсы", keys: "баланс лояльность бронь case банк времени" },
  { group: "Панель", id: "settings", href: "settings.html", title: "Настройки", keys: "лояльность награды акция kaspi сброс" },
  { group: "Панель", id: "telegram", href: "telegram.html", title: "Telegram", keys: "бот mini app рассылка алерты" },
  { group: "Панель", id: "shell", href: "shell.html", title: "Оболочка на ПК", keys: "lock qr игры новости вызов" },
  { group: "Панель", id: "screens", href: "screens.html", title: "Экраны зала и прайс", keys: "телевизор промо display price" },
  { group: "Панель", id: "staff", href: "staff.html", title: "Сотрудники, отчёты, ПК", keys: "зарплата мониторинг обновления новости программы аудит" },
  { group: "Если что-то не так", id: "limits", href: "limits.html", title: "Чего в продукте нет", keys: "границы ccboot офд облако" },
  { group: "Если что-то не так", id: "troubles", href: "troubles.html", title: "Если не запускается", keys: "postgres порт лицензия офлайн" }
];

function productHref() {
  const host = location.hostname.toLowerCase();
  if (host === "wiki.shift-club.kz" || host === "www.wiki.shift-club.kz")
    return "https://product.shift-club.kz/";
  if (host === "shift-club.kz" || host === "www.shift-club.kz" || host === "localhost" || host === "127.0.0.1")
    return "/product/";
  return "https://product.shift-club.kz/";
}

function renderChrome() {
  const current = document.body.dataset.page || "index";
  const top = document.getElementById("top");
  const side = document.getElementById("side");
  if (!top || !side) return;

  top.innerHTML = `
    <button class="menu-btn" type="button" id="menuBtn">Разделы</button>
    <a class="brand" href="index.html">SHIFT <span>инструкция</span></a>
    <div class="top-search">
      <input id="q" type="search" placeholder="Найти раздел: касса, логотип, лицензия…" autocomplete="off" />
      <div class="search-hit" id="hits"></div>
    </div>
    <div class="top-links">
      <a class="keep" href="${productHref()}">О продукте</a>
    </div>`;

  const groups = [];
  for (const page of PAGES) {
    if (!groups.length || groups[groups.length - 1].name !== page.group)
      groups.push({ name: page.group, items: [] });
    groups[groups.length - 1].items.push(page);
  }
  side.innerHTML = groups.map(group => `
    <h2>${group.name}</h2>
    ${group.items.map(page => `<a class="${page.id === current ? "is-on" : ""}" href="${page.href}">${page.title}</a>`).join("")}
  `).join("");

  document.getElementById("menuBtn").addEventListener("click", () => {
    document.body.classList.toggle("nav-open");
  });
  side.querySelectorAll("a").forEach(link => {
    link.addEventListener("click", () => document.body.classList.remove("nav-open"));
  });

  const input = document.getElementById("q");
  const hits = document.getElementById("hits");
  input.addEventListener("input", () => {
    const query = input.value.trim().toLowerCase();
    if (query.length < 2) {
      hits.style.display = "none";
      hits.innerHTML = "";
      return;
    }
    const found = PAGES.filter(page =>
      (page.title + " " + page.keys + " " + page.group).toLowerCase().includes(query)
    ).slice(0, 8);
    hits.innerHTML = found.length
      ? found.map(page => `<a href="${page.href}"><strong>${page.title}</strong><small>${page.group}</small></a>`).join("")
      : `<a href="index.html">Ничего не нашли<small>Откройте оглавление</small></a>`;
    hits.style.display = "block";
  });
  document.addEventListener("click", (event) => {
    if (!event.target.closest(".top-search")) hits.style.display = "none";
  });
}

renderChrome();
