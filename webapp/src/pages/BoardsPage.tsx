import { useCallback, useEffect, useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Board } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { RoleBadge } from '../components/RoleBadge';

export function BoardsPage(): ReactElement {
  const { user, subscription, logout } = useAuth();
  const [boards, setBoards] = useState<Board[]>([]);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setBoards(await api<Board[]>('/boards'));
      setError(null);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось загрузить доски.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      const board = await api<Board>('/boards', { method: 'POST', body: { name } });
      setBoards((current) => [board, ...current]);
      setName('');
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось создать доску.');
    } finally {
      setBusy(false);
    }
  };

  const remove = async (board: Board) => {
    if (!window.confirm(`Удалить доску «${board.name}»? Это действие необратимо.`)) {
      return;
    }

    try {
      await api(`/boards/${board.id}`, { method: 'DELETE' });
      setBoards((current) => current.filter((x) => x.id !== board.id));
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось удалить доску.');
    }
  };

  return (
    <div className="page">
      <header className="page-header">
        <h1>Мои доски</h1>
        <div className="row">
          <span className="muted small">{user?.email}</span>
          <button className="button ghost" type="button" onClick={logout}>
            Выйти
          </button>
        </div>
      </header>

      {subscription && !subscription.active ? (
        <p className="banner">
          Пробный период закончился. Создавать новые доски пока нельзя — доски,
          куда вас пригласили, остаются доступны.
        </p>
      ) : null}

      {subscription?.status === 'trialing' && subscription.trialEndsAt ? (
        <p className="banner muted">
          Пробный период до {new Date(subscription.trialEndsAt).toLocaleDateString('ru-RU')}.
        </p>
      ) : null}

      <form className="row create-form" onSubmit={create}>
        <input
          type="text"
          value={name}
          placeholder="Название новой доски"
          onChange={(event) => setName(event.target.value)}
        />
        <button className="button" type="submit" disabled={busy}>
          Создать
        </button>
      </form>

      {error ? <p className="error">{error}</p> : null}

      {loading ? (
        <p className="muted">Загружаем…</p>
      ) : boards.length === 0 ? (
        <p className="muted">Досок пока нет. Создайте первую или дождитесь приглашения.</p>
      ) : (
        <ul className="board-list">
          {boards.map((board) => (
            <li key={board.id} className="card board-row">
              <div>
                <Link className="board-name" to={`/boards/${board.id}`}>
                  {board.name}
                </Link>
                <p className="muted small">
                  Участников: {board.memberCount} · изменена{' '}
                  {new Date(board.modifiedAt).toLocaleString('ru-RU')}
                </p>
              </div>

              <div className="row">
                <RoleBadge role={board.role} />
                {board.canManage ? (
                  <button className="button ghost danger" type="button" onClick={() => void remove(board)}>
                    Удалить
                  </button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
