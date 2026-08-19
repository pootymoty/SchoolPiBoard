import { useEffect, useRef, useState } from 'react';
import type { ReactElement } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { Page } from '../components/Layout';

type State = 'checking' | 'done' | 'failed';

/** Страница, на которую ведёт ссылка из письма о регистрации. */
export function ConfirmPage(): ReactElement {
  const [params] = useSearchParams();
  const [state, setState] = useState<State>('checking');
  const [message, setMessage] = useState('');
  const started = useRef(false);

  useEffect(() => {
    // В StrictMode эффект выполняется дважды — второй запрос уже не нужен.
    if (started.current) return;
    started.current = true;

    const token = params.get('token');
    if (!token) {
      setState('failed');
      setMessage('Ссылка неполная. Откройте её из письма целиком.');
      return;
    }

    api<{ message: string }>('/auth/confirm', { method: 'POST', body: { token } })
      .then((result) => {
        setState('done');
        setMessage(result.message);
      })
      .catch((reason: unknown) => {
        setState('failed');
        setMessage(reason instanceof ApiError ? reason.message : 'Не удалось подтвердить почту.');
      });
  }, [params]);

  return (
    <Page>
      <div className="card form">
        <h1>Подтверждение почты</h1>

        {state === 'checking' ? <p className="muted">Проверяем ссылку…</p> : null}

        {state === 'done' ? (
          <>
            <p>{message}</p>
            <Link className="button" to="/login?confirmed=1">Войти</Link>
          </>
        ) : null}

        {state === 'failed' ? (
          <>
            <p className="error">{message}</p>
            <Link className="button" to="/register">Зарегистрироваться заново</Link>
          </>
        ) : null}
      </div>
    </Page>
  );
}
