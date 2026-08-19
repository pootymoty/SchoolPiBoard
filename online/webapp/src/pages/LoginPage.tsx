import { useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Page } from '../components/Layout';

export function LoginPage(): ReactElement {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const justRegistered = params.get('registered') === '1';
  const justConfirmed = params.get('confirmed') === '1';
  const next = params.get('next');

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await login(email, password);
      navigate(next ?? '/subscribe');
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось войти.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Page>
      <form className="card form" onSubmit={submit}>
        <h1>Вход</h1>

        {justRegistered ? (
          <p className="banner">
            Мы отправили письмо со ссылкой подтверждения. Перейдите по ней —
            учётная запись создастся, и можно будет войти.
            Ссылка действует час, потом регистрацию нужно пройти заново.
          </p>
        ) : null}

        {justConfirmed ? <p className="banner">Почта подтверждена. Теперь можно войти.</p> : null}

        <label htmlFor="email">Почта</label>
        <input id="email" type="email" required autoComplete="email"
               value={email} onChange={(event) => setEmail(event.target.value)} />

        <label htmlFor="password">Пароль</label>
        <input id="password" type="password" required autoComplete="current-password"
               value={password} onChange={(event) => setPassword(event.target.value)} />

        {error ? <p className="error">{error}</p> : null}

        <button className="button" type="submit" disabled={busy}>
          {busy ? 'Проверяем…' : 'Войти'}
        </button>

        <p className="muted small">
          Нет учётной записи? <Link to="/register">Зарегистрироваться</Link>
        </p>
      </form>
    </Page>
  );
}
