import { useCallback, useEffect, useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Board, BoardPage as BoardPageData } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Page } from '../components/Layout';
import { RoleBadge } from '../components/RoleBadge';
import { Pagination } from '../components/Pagination';

export function BoardsPage(): ReactElement {
  const { subscription } = useAuth();

  const [data, setData] = useState<BoardPageData | null>(null);
  const [page, setPage] = useState(1);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (target: number) => {
    setLoading(true);
    try {
      setData(await api<BoardPageData>(`/boards?page=${target}`));
      setError(null);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось загрузить доски.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load(page);
  }, [load, page]);

  const create = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await api<Board>('/boards', { method: 'POST', body: { name } });
      setName('');
      setPage(1);
      await load(1);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось создать доску.');
    } finally {
      setBusy(false);
    }
  };

  const remove = async (board: Board) => {
    if (!window.confirm(`Удалить доску «${board.name}»? Это действие необратимо.`)) return;

    try {
      await api(`/boards/${board.id}`, { method: 'DELETE' });
      await load(page);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось удалить доску.');
    }
  };

  return (
    <Page wide>
      <div className="page-header">
        <h1>Мои доски</h1>
        {data ? <span className="muted small">Всего: {data.total}</span> : null}
      </div>

      {subscription && !subscription.active ? (
        <p className="banner">
          Подписка закончилась — новые доски создать нельзя. Доски, куда вас
          пригласили, остаются доступны. <Link to="/subscribe">Продлить</Link>
        </p>
      ) : null}

      <form className="row create-form" onSubmit={create}>
        <input type="text" value={name} placeholder="Название новой доски"
               onChange={(event) => setName(event.target.value)} />
        <button className="button" type="submit" disabled={busy}>Создать</button>
      </form>

      {error ? <p className="error">{error}</p> : null}

      {loading ? (
        <p className="muted">Загружаем…</p>
      ) : !data || data.items.length === 0 ? (
        <p className="muted">Досок пока нет. Создайте первую или дождитесь приглашения.</p>
      ) : (
        <>
          <ul className="board-list">
            {data.items.map((board) => (
              <li key={board.id} className="card board-row">
                <div>
                  <Link className="board-name" to={`/boards/${board.id}`}>
                    {/* Ссылка рядом с названием — признак чужой доски. */}
                    {board.invited ? <span className="link-mark" title="Доступ по приглашению">🔗</span> : null}
                    {board.name}
                  </Link>
                  <p className="muted small">
                    Участников: {board.memberCount} · изменена{' '}
                    {new Date(board.modifiedAt).toLocaleString('ru-RU')}
                    {board.editUntil && board.role !== 'viewer' ? (
                      <> · правки до {new Date(board.editUntil).toLocaleDateString('ru-RU')}</>
                    ) : null}
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

          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            total={data.total}
            onChange={setPage}
          />
        </>
      )}
    </Page>
  );
}
