import type { ReactElement } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useBoardHub } from '../realtime/useBoardHub';
import { MembersPanel } from '../components/MembersPanel';
import { PresenceBar } from '../components/PresenceBar';
import { RoleBadge } from '../components/RoleBadge';

const STATUS_LABELS: Record<string, string> = {
  connecting: 'подключаемся…',
  connected: 'на связи',
  reconnecting: 'связь потеряна, восстанавливаем…',
  disconnected: 'нет связи',
  error: 'ошибка подключения',
};

export function BoardPage(): ReactElement {
  const { boardId } = useParams<{ boardId: string }>();
  const { user } = useAuth();
  const { status, error, board, participants } = useBoardHub(boardId);

  return (
    <div className="page board-page">
      <header className="page-header">
        <div className="row">
          <Link className="button ghost" to="/">
            ← К доскам
          </Link>
          <h1>{board?.name ?? 'Доска'}</h1>
          {board ? <RoleBadge role={board.role} /> : null}
        </div>

        <div className="row">
          <PresenceBar participants={participants} />
          <span className={`status status-${status}`}>{STATUS_LABELS[status] ?? status}</span>
        </div>
      </header>

      {error ? <p className="error">{error}</p> : null}

      <div className="board-layout">
        <section className="card canvas-placeholder">
          {board?.canEdit ? (
            <p className="muted">
              Здесь появится холст (этап 2b). Роль позволяет рисовать.
            </p>
          ) : (
            <p className="muted">
              Здесь появится холст (этап 2b). У вас доступ только на просмотр —
              инструменты рисования будут скрыты.
            </p>
          )}
          <p className="muted small">
            Участников в доске сейчас: {participants.length}
          </p>
        </section>

        {boardId && user ? (
          <MembersPanel
            boardId={boardId}
            canManage={board?.canManage ?? false}
            currentUserId={user.id}
          />
        ) : null}
      </div>
    </div>
  );
}
