import { useCallback, useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { Captcha } from '../components/Captcha';
import { Page } from '../components/Layout';

const MIN_PASSWORD_LENGTH = 8;

export function RegisterPage(): ReactElement {
  const navigate = useNavigate();

  const [lastName, setLastName] = useState('');
  const [firstName, setFirstName] = useState('');
  const [birthDate, setBirthDate] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [passwordConfirm, setPasswordConfirm] = useState('');
  const [captchaToken, setCaptchaToken] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const onToken = useCallback((token: string) => setCaptchaToken(token), []);

  const submit = async (event: FormEvent) => {
    event.preventDefault();

    if (password.length < MIN_PASSWORD_LENGTH) {
      setError(`Пароль должен быть не короче ${MIN_PASSWORD_LENGTH} символов.`);
      return;
    }

    if (password !== passwordConfirm) {
      setError('Пароли не совпадают.');
      return;
    }

    setBusy(true);
    setError(null);

    try {
      await api('/auth/register', {
        method: 'POST',
        body: { lastName, firstName, birthDate, email, password, passwordConfirm, captchaToken },
      });

      // Учётной записи ещё нет — она появится после перехода по ссылке из письма.
      navigate('/login?registered=1');
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось зарегистрироваться.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Page>
      <form className="card form" onSubmit={submit}>
        <h1>Регистрация</h1>

        <label htmlFor="lastName">Фамилия</label>
        <input id="lastName" type="text" required autoComplete="family-name"
               value={lastName} onChange={(event) => setLastName(event.target.value)} />

        <label htmlFor="firstName">Имя</label>
        <input id="firstName" type="text" required autoComplete="given-name"
               value={firstName} onChange={(event) => setFirstName(event.target.value)} />

        <label htmlFor="birthDate">Дата рождения</label>
        <input id="birthDate" type="date" required
               value={birthDate} onChange={(event) => setBirthDate(event.target.value)} />

        <label htmlFor="email">Почта</label>
        <input id="email" type="email" required autoComplete="email"
               value={email} onChange={(event) => setEmail(event.target.value)} />

        <label htmlFor="password">Пароль</label>
        <input id="password" type="password" required minLength={MIN_PASSWORD_LENGTH} autoComplete="new-password"
               value={password} onChange={(event) => setPassword(event.target.value)} />

        <label htmlFor="passwordConfirm">Повторите пароль</label>
        <input id="passwordConfirm" type="password" required autoComplete="new-password"
               value={passwordConfirm} onChange={(event) => setPasswordConfirm(event.target.value)} />

        <Captcha onToken={onToken} />

        {error ? <p className="error">{error}</p> : null}

        <button className="button" type="submit" disabled={busy}>
          {busy ? 'Отправляем…' : 'Зарегистрироваться'}
        </button>

        <p className="muted small">
          Регистрируясь, вы соглашаетесь с <Link to="/legal/terms">условиями использования</Link> и{' '}
          <Link to="/legal/privacy">обработкой персональных данных</Link>.
        </p>

        <p className="muted small">
          Уже есть учётная запись? <Link to="/login">Войти</Link>
        </p>
      </form>
    </Page>
  );
}
