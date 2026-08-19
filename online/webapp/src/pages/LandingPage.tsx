import type { ReactElement } from 'react';
import { Link } from 'react-router-dom';
import { BookshelfBackground } from '../components/BookshelfBackground';
import { Footer, Header } from '../components/Layout';

/**
 * Главная страница. Единственная, где есть фон с книжными полками —
 * на остальных страницах он только мешал бы работать.
 */
export function LandingPage(): ReactElement {
  return (
    <div className="site landing">
      <BookshelfBackground />

      <div className="landing-content">
        <Header />

        <main className="main">
          <section className="hero">
            <h1 className="hero-title">Онлайн-доска для занятий</h1>
            <p className="hero-lead">
              Объясняйте, рисуйте и разбирайте задачи вместе с учениками —
              прямо в браузере, всем классом на одной доске.
            </p>

            <div className="hero-actions">
              <Link className="hero-button" to="/login">ВОЙТИ</Link>
              <Link className="hero-button outline" to="/register">ЗАРЕГИСТРИРОВАТЬСЯ</Link>
            </div>

            <p className="muted small">Первые 7 дней бесплатно, карта не нужна.</p>
          </section>

          <section className="cards">
            <article className="card">
              <h2>Для репетиторов</h2>
              <p className="muted">
                Ученик видит доску в реальном времени: не нужно фотографировать
                тетрадь и пересылать в мессенджер.
              </p>
            </article>

            <article className="card">
              <h2>Для учителей</h2>
              <p className="muted">
                Пригласите класс ссылкой. До двадцати участников на одной доске,
                у каждого свой курсор.
              </p>
            </article>

            <article className="card">
              <h2>Для учеников</h2>
              <p className="muted">
                Подписка нужна только тому, кто создаёт доски. Чтобы работать
                на чужой доске, достаточно приглашения.
              </p>
            </article>
          </section>

          <section className="section">
            <h2>Как это работает</h2>
            <ol className="steps">
              <li>Зарегистрируйтесь и подтвердите почту.</li>
              <li>Возьмите 7 бесплатных дней или сразу оформите подписку.</li>
              <li>Создайте доску и пригласите участников по почте или ссылкой.</li>
              <li>Рисуйте вместе: правки видны всем сразу.</li>
            </ol>
          </section>

          <section className="section">
            <h2>Сколько стоит</h2>
            <div className="cards">
              <div className="card price-card"><b>30 дней</b><span>499 ₽</span></div>
              <div className="card price-card"><b>90 дней</b><span>1449 ₽</span></div>
              <div className="card price-card"><b>180 дней</b><span>2799 ₽</span></div>
              <div className="card price-card"><b>365 дней</b><span>5399 ₽</span></div>
            </div>
            <p className="muted small">
              Оплата разовая за выбранный срок. Автопродление можно включить
              и отключить в настройках профиля.
            </p>
          </section>

          <section className="section">
            <h2>Правовая информация</h2>
            <p className="muted">
              <Link to="/legal/terms">Условия использования</Link> ·{' '}
              <Link to="/legal/privacy">Обработка персональных данных</Link> ·{' '}
              <Link to="/legal/offer">Оферта</Link>
            </p>
          </section>
        </main>

        <Footer />
      </div>
    </div>
  );
}
