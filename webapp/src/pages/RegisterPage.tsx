import { useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../api/client';

const MIN_PASSWORD_LENGTH = 8;

export function RegisterPage(): ReactElement {
  const { register } = useAuth();
  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();

    if (password.length < MIN_PASSWORD_LENGTH) {
      setError(`Пароль должен быть не короче ${MIN_PASSWORD_LENGTH} символов.`);
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await register(email, password, name);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось зарегистрироваться.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="screen-center">
      <form className="card form" onSubmit={submit}>
        <h1>Регистрация</h1>
        <p className="muted small">Первые 7 дней — бесплатно, карта не нужна.</p>

        <label htmlFor="email">Почта</label>
        <input
          id="email"
          type="email"
          value={email}
          autoComplete="email"
          required
          onChange={(event) => setEmail(event.target.value)}
        />

        <label htmlFor="name">Имя (видят участники досок)</label>
        <input
          id="name"
          type="text"
          value={name}
          autoComplete="name"
          onChange={(event) => setName(event.target.value)}
        />

        <label htmlFor="password">Пароль</label>
        <input
          id="password"
          type="password"
          value={password}
          autoComplete="new-password"
          required
          minLength={MIN_PASSWORD_LENGTH}
          onChange={(event) => setPassword(event.target.value)}
        />

        {error ? <p className="error">{error}</p> : null}

        <button className="button" type="submit" disabled={busy}>
          {busy ? 'Создаём…' : 'Создать учётную запись'}
        </button>

        <p className="muted small">
          Уже есть учётная запись? <Link to="/login">Войти</Link>
        </p>
      </form>
    </div>
  );
}
