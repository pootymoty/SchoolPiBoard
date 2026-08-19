import type { ReactElement, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

/** Шапка с логотипом и названием платформы — общая для всех страниц. */
export function Header(): ReactElement {
  const { user, logout } = useAuth();

  return (
    <header className="site-header">
      <Link className="logo" to={user ? '/boards' : '/'}>
        <span className="logo-mark" aria-hidden="true">π</span>
        <span className="logo-text">SchoolPiBoard</span>
      </Link>

      <nav className="row">
        {user ? (
          <>
            <Link className="nav-link" to="/boards">Мои доски</Link>
            <Link className="nav-link" to="/profile">Профиль</Link>
            <button className="button ghost" type="button" onClick={logout}>Выйти</button>
          </>
        ) : (
          <>
            <Link className="nav-link" to="/login">Войти</Link>
            <Link className="button" to="/register">Регистрация</Link>
          </>
        )}
      </nav>
    </header>
  );
}

export function Footer(): ReactElement {
  return (
    <footer className="site-footer">
      <div className="row">
        <Link to="/legal/terms">Условия использования</Link>
        <Link to="/legal/privacy">Обработка персональных данных</Link>
        <Link to="/legal/offer">Оферта</Link>
      </div>
      <p className="muted small">
        SchoolPiBoard · school-pi-board.online · ЗАГЛУШКА: реквизиты продавца
      </p>
    </footer>
  );
}

/** Обычная страница: шапка, содержимое, подвал. */
export function Page({ children, wide }: { children: ReactNode; wide?: boolean }): ReactElement {
  return (
    <div className="site">
      <Header />
      <main className={wide ? 'main wide' : 'main'}>{children}</main>
      <Footer />
    </div>
  );
}
