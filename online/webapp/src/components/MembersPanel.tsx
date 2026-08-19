import { useCallback, useEffect, useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { api, ApiError } from '../api/client';
import type { BoardRole, Member } from '../api/types';
import { RoleBadge } from './RoleBadge';
import { InvitePanel } from './InvitePanel';

interface Props {
  boardId: string;
  /** Управлять составом может только владелец. */
  canManage: boolean;
  currentUserId: string;
}

export function MembersPanel({ boardId, canManage, currentUserId }: Props): ReactElement {
  const [members, setMembers] = useState<Member[]>([]);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<BoardRole>('editor');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setMembers(await api<Member[]>(`/boards/${boardId}/members`));
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось загрузить участников.');
    }
  }, [boardId]);

  useEffect(() => {
    void load();
  }, [load]);

  const invite = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await api<Member>(`/boards/${boardId}/members`, { method: 'POST', body: { email, role } });
      setEmail('');
      await load();
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось пригласить.');
    } finally {
      setBusy(false);
    }
  };

  const changeRole = async (member: Member, next: BoardRole) => {
    setError(null);
    try {
      await api<Member>(`/boards/${boardId}/members/${member.userId}`, { method: 'PATCH', body: { role: next } });
      await load();
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось изменить роль.');
    }
  };

  const remove = async (member: Member) => {
    setError(null);
    try {
      await api(`/boards/${boardId}/members/${member.userId}`, { method: 'DELETE' });
      await load();
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось убрать участника.');
    }
  };

  return (
    <aside className="card panel">
      <h2>Участники</h2>

      <ul className="member-list">
        {members.map((member) => (
          <li key={member.userId} className="member-row">
            <div>
              <div className="member-name">
                {member.name}
                {member.userId === currentUserId ? <span className="muted small"> — это вы</span> : null}
              </div>
              <div className="muted small">{member.email}</div>
              {member.viaLink ? (
                <div className="muted small">
                  по ссылке
                  {member.editUntil ? ` · правки до ${new Date(member.editUntil).toLocaleDateString('ru-RU')}` : ''}
                </div>
              ) : null}
            </div>

            {canManage && member.role !== 'owner' ? (
              <div className="row">
                <select value={member.role} onChange={(event) => void changeRole(member, event.target.value as BoardRole)}>
                  <option value="editor">редактор</option>
                  <option value="viewer">просмотр</option>
                </select>
                <button className="button ghost danger" type="button" onClick={() => void remove(member)}>
                  Убрать
                </button>
              </div>
            ) : (
              <RoleBadge role={member.role} />
            )}
          </li>
        ))}
      </ul>

      {canManage ? (
        <>
          <form className="invite-form" onSubmit={invite}>
            <label htmlFor="invite-email">Пригласить по почте</label>
            <input id="invite-email" type="email" required placeholder="uchenik@example.com"
                   value={email} onChange={(event) => setEmail(event.target.value)} />

            <div className="row">
              <select value={role} onChange={(event) => setRole(event.target.value as BoardRole)}>
                <option value="editor">редактор</option>
                <option value="viewer">просмотр</option>
              </select>
              <button className="button" type="submit" disabled={busy}>Пригласить</button>
            </div>

            <p className="muted small">По почте можно пригласить того, кто уже зарегистрирован.</p>
          </form>

          <InvitePanel boardId={boardId} />
        </>
      ) : null}

      {error ? <p className="error">{error}</p> : null}
    </aside>
  );
}
