import { useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';

export function LoginPage(): ReactElement {
  const { login } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await login(email, password);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось войти.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="screen-center">
      <form className="card form" onSubmit={submit}>
        <h1>Вход</h1>

        <label htmlFor="email">Почта</label>
        <input
          id="email"
          type="email"
          value={email}
          autoComplete="email"
          required
          onChange={(event) => setEmail(event.target.value)}
        />

        <label htmlFor="password">Пароль</label>
        <input
          id="password"
          type="password"
          value={password}
          autoComplete="current-password"
          required
          onChange={(event) => setPassword(event.target.value)}
        />

        {error ? <p className="error">{error}</p> : null}

        <button className="button" type="submit" disabled={busy}>
          {busy ? 'Проверяем…' : 'Войти'}
        </button>

        <p className="muted small">
          Нет учётной записи? <Link to="/register">Зарегистрироваться</Link>
        </p>
      </form>
    </div>
  );
}
