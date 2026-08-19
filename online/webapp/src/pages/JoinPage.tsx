import { useEffect, useRef, useState } from 'react';
import type { ReactElement } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Board } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Page } from '../components/Layout';

/** Страница ссылки-приглашения: /join/{token}. */
export function JoinPage(): ReactElement {
  const { token } = useParams<{ token: string }>();
  const { user, loading } = useAuth();
  const navigate = useNavigate();

  const [boardName, setBoardName] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const joined = useRef(false);

  // Что за доска — показываем и тем, кто ещё не вошёл: человек должен
  // понимать, куда его зовут, прежде чем регистрироваться.
  useEffect(() => {
    if (!token) return;

    api<{ boardName: string }>(`/invites/${token}`)
      .then((result) => setBoardName(result.boardName))
      .catch((reason: unknown) => {
        setError(reason instanceof ApiError ? reason.message : 'Ссылка недействительна.');
      });
  }, [token]);

  useEffect(() => {
    if (loading || !user || !token || joined.current || error) return;

    joined.current = true;
    setBusy(true);

    api<Board>(`/invites/${token}/join`, { method: 'POST' })
      .then((board) => navigate(`/boards/${board.id}`, { replace: true }))
      .catch((reason: unknown) => {
        setError(reason instanceof ApiError ? reason.message : 'Не удалось присоединиться.');
        setBusy(false);
      });
  }, [loading, user, token, navigate, error]);

  return (
    <Page>
      <div className="card form">
        <h1>Приглашение на доску</h1>

        {boardName ? <p>Вас приглашают на доску «{boardName}».</p> : null}
        {error ? <p className="error">{error}</p> : null}

        {!error && user ? (
          <p className="muted">{busy ? 'Открываем доску…' : 'Готово.'}</p>
        ) : null}

        {!error && !user && !loading ? (
          <>
            <p className="muted">Чтобы открыть доску, войдите или зарегистрируйтесь.</p>
            <div className="row">
              <Link className="button" to={`/login?next=/join/${token ?? ''}`}>Войти</Link>
              <Link className="button ghost" to="/register">Зарегистрироваться</Link>
            </div>
            <p className="muted small">Подписка для этого не нужна.</p>
          </>
        ) : null}
      </div>
    </Page>
  );
}
