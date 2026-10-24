import { Link } from 'react-router-dom'
import type { Me } from '../types'

const steps = [
  {
    n: '01',
    title: 'Гость открывает страницу',
    text: 'Ссылка или QR на телефоне. Аккаунт не нужен: человек сразу видит кейс клуба.',
  },
  {
    n: '02',
    title: 'Кейс крутится у вас на сайте',
    text: 'Приз выбирается на сервере. На экране остаётся название и короткий код.',
  },
  {
    n: '03',
    title: 'Сотрудник зачисляет у себя',
    text: 'В SENET, GameClass, Bada или любой другой кассе. В кабинете CLUTCH он отмечает «зачислил».',
  },
]

const points = [
  { k: 'Браузер', v: 'Клуб ничего не ставит на свой сервер и не пускает нас в свою базу.' },
  { k: 'Свой приз', v: 'Бонус, минуты, бар или вещь со склада. У каждого приза своя подсказка для кассы.' },
  { k: 'Два входа', v: 'Админ платформы заводит клубы. В клубе есть владелец и сотрудник смены.' },
  { k: 'Код или открытая страница', v: 'Можно пустить всех по ссылке или выдавать одноразовый код на стойке.' },
]

export function Landing({ me }: { me: Me | null | undefined }) {
  const cabinet = me?.role === 'admin' ? '/admin' : me?.role === 'club' ? '/desk' : '/login'
  return (
    <div className="site">
      <header className="nav">
        <Link to="/" className="brand">CLUTCH</Link>
        <nav>
          <a href="#how">Как это идёт</a>
          <a href="#floor">На стойке</a>
          <a href="#setup">Настройки</a>
        </nav>
        <Link className="nav-btn" to={cabinet}>{me ? 'Кабинет' : 'Войти'}</Link>
      </header>

      <section className="hero">
        <div className="hero-copy">
          <p className="eyebrow">Кейсы для компьютерного клуба</p>
          <h1>Гость крутит.<br />Касса остаётся вашей.</h1>
          <p className="lead">
            Страница кейса живёт у нас. Приз показывается на экране. Сотрудник вносит бонус,
            время или товар в той программе, которой клуб уже пользуется.
          </p>
          <div className="hero-actions">
            <Link className="btn btn-acid" to="/c/nexus/smena">Открыть демо-кейс</Link>
            <a className="btn btn-ghost" href="#how">Посмотреть схему</a>
          </div>
        </div>
        <figure className="hero-shot">
          <img src="/art/hero-crate.jpg" alt="Чёрный кейс с золотыми углами и зелёной щелью света" />
        </figure>
      </section>

      <section className="marquee" aria-hidden="true">
        <p>CS-ритм стойки · руны редкости · бонус · время · бар · код гостя · смена видит очередь</p>
      </section>

      <section className="block" id="how">
        <div className="block-head">
          <p className="eyebrow">Три шага в смене</p>
          <h2>От ссылки до записи в вашей кассе.</h2>
        </div>
        <div className="steps">
          {steps.map((step) => (
            <article key={step.n}>
              <span>{step.n}</span>
              <h3>{step.title}</h3>
              <p>{step.text}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="arena">
        <img src="/art/arena-night.jpg" alt="Тёмный зал компьютерного клуба с зелёной подсветкой" />
        <div>
          <p className="eyebrow">На сервере клуба пусто</p>
          <h2>Программа учёта не меняется.</h2>
          <p>
            SENET, GameClass, Bada и любая другая касса остаются как есть. CLUTCH не читает их продажи
            и не пишет им в базу. Связь между призом и гостем делает человек на стойке: он видит код
            и зачисляет так, как зачисляет обычный бонус.
          </p>
        </div>
      </section>

      <section className="block" id="floor">
        <div className="block-head">
          <p className="eyebrow">Что видит смена</p>
          <h2>Очередь призов, а не ещё одна касса.</h2>
        </div>
        <div className="split">
          <figure>
            <img src="/art/operator.jpg" alt="Администратор клуба в тёмном зале" />
          </figure>
          <div className="stack-list">
            <article>
              <h3>Код на экране гостя</h3>
              <p>Четыре символа. Гость показывает телефон, сотрудник находит то же открытие у себя.</p>
            </article>
            <article>
              <h3>Подсказка под их программу</h3>
              <p>У приза написано, что именно ввести: «бонус +200» или «30 минут общего зала».</p>
            </article>
            <article>
              <h3>Кнопка «Зачислил»</h3>
              <p>После записи в своей кассе сотрудник закрывает строку. Утро показывает, что зависло.</p>
            </article>
          </div>
        </div>
      </section>

      <section className="block prizes-band">
        <div className="block-head">
          <p className="eyebrow">Призы</p>
          <h2>Редкость, вес и лимит на день.</h2>
        </div>
        <div className="prize-row">
          {[
            ['/art/prize-time.jpg', 'Время', 'Минуты и пакеты, которые смена и так умеет ставить.'],
            ['/art/prize-bonus.jpg', 'Бонус', 'Сумма на бонусный счёт. Круглая, чтобы не считать.'],
            ['/art/prize-drink.jpg', 'Бар', 'Напиток отдают с полки и отмечают выдачу.'],
            ['/art/prize-headset.jpg', 'Склад', 'Редкая вещь с лимитом, чтобы гарнитура не кончилась к вечеру.'],
          ].map(([src, title, text]) => (
            <article key={title}>
              <img src={src} alt="" />
              <h3>{title}</h3>
              <p>{text}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="block" id="setup">
        <div className="block-head">
          <p className="eyebrow">Гибко для каждого клуба</p>
          <h2>Один сервис. Свои правила.</h2>
        </div>
        <div className="points">
          {points.map((point) => (
            <article key={point.k}>
              <h3>{point.k}</h3>
              <p>{point.v}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="roles">
        <article>
          <p className="eyebrow">Админ</p>
          <h2>Заводит клубы.</h2>
          <p>Платформа видит, кто подключён, и может остановить клуб, не трогая чужую кассу.</p>
        </article>
        <article>
          <p className="eyebrow">Клуб</p>
          <h2>Владелец и смена.</h2>
          <p>Владелец собирает кейс, призы и людей. Сотрудник в смене выдаёт коды и отмечает зачисление.</p>
        </article>
      </section>

      <section className="cta">
        <div>
          <p className="eyebrow">Демо уже крутится</p>
          <h2>Откройте кейс Nexus Arena.</h2>
          <p>Это учебный клуб внутри сервиса. Приз останется в очереди смены, в чужую кассу он сам не уйдёт.</p>
        </div>
        <Link className="btn btn-acid" to="/c/nexus/smena">Крутить демо</Link>
      </section>

      <footer className="foot">
        <span>CLUTCH</span>
        <span>Приз зачисляет клуб. Сервис деньги гостю не выплачивает.</span>
      </footer>
    </div>
  )
}
