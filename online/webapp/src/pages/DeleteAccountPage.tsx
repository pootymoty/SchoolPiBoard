import { useEffect, useRef, useState } from 'react';
import type { ReactElement } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { Page } from '../components/Layout';

type State = 'checking' | 'done' | 'failed';

/** Страница из письма об удалении учётной записи: /profile/delete?token=… */
export function DeleteAccountPage(): ReactElement {
  const [params] = useSearchParams();
  const { logout } = useAuth();
  const [state, setState] = useState<State>('checking');
  const [message, setMessage] = useState('');
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const token = params.get('token');
    if (!token) {
      setState('failed');
      setMessage('Ссылка неполная. Откройте её из письма целиком.');
      return;
    }

    api<{ message: string }>('/profile/delete-confirm', { method: 'POST', body: { token } })
      .then((result) => {
        setState('done');
        setMessage(result.message);
        // Учётной записи больше нет — токен входа держать незачем.
        logout();
      })
      .catch((reason: unknown) => {
        setState('failed');
        setMessage(reason instanceof ApiError ? reason.message : 'Не удалось удалить учётную запись.');
      });
  }, [params, logout]);

  return (
    <Page>
      <div className="card form">
        <h1>Удаление учётной записи</h1>

        {state === 'checking' ? <p className="muted">Проверяем ссылку…</p> : null}
        {state === 'done' ? <p>{message}</p> : null}
        {state === 'failed' ? <p className="error">{message}</p> : null}

        <Link className="button" to="/">На главную</Link>
      </div>
    </Page>
  );
}
