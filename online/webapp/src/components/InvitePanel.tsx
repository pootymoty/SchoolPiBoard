import { useCallback, useEffect, useState } from 'react';
import type { ReactElement } from 'react';
import { api, ApiError } from '../api/client';
import type { BoardRole, Invite } from '../api/types';

/**
 * Ссылки-приглашения.
 *
 * Саму ссылку сервер показывает один раз — при создании: в базе лежит только
 * её хеш. Поэтому созданную ссылку сразу предлагаем скопировать.
 */
export function InvitePanel({ boardId }: { boardId: string }): ReactElement {
  const [invites, setInvites] = useState<Invite[]>([]);
  const [role, setRole] = useState<BoardRole>('editor');
  const [fresh, setFresh] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setInvites(await api<Invite[]>(`/boards/${boardId}/invites`));
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось загрузить ссылки.');
    }
  }, [boardId]);

  useEffect(() => {
    void load();
  }, [load]);

  const create = async () => {
    setBusy(true);
    setError(null);
    setCopied(false);

    try {
      const invite = await api<Invite>(`/boards/${boardId}/invites`, { method: 'POST', body: { role } });
      setFresh(invite.url);
      await load();
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось создать ссылку.');
    } finally {
      setBusy(false);
    }
  };

  const revoke = async (invite: Invite) => {
    setError(null);
    try {
      await api(`/boards/${boardId}/invites/${invite.id}`, { method: 'DELETE' });
      setFresh(null);
      await load();
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось отозвать ссылку.');
    }
  };

  const copy = async () => {
    if (!fresh) return;
    try {
      await navigator.clipboard.writeText(fresh);
      setCopied(true);
    } catch {
      // Буфер может быть недоступен — ссылка и так видна на экране.
      setCopied(false);
    }
  };

  return (
    <div className="invite-form">
      <label htmlFor="invite-role">Ссылка-приглашение</label>

      <div className="row">
        <select id="invite-role" value={role} onChange={(event) => setRole(event.target.value as BoardRole)}>
          <option value="editor">редактор</option>
          <option value="viewer">просмотр</option>
        </select>
        <button className="button" type="button" disabled={busy} onClick={() => void create()}>
          Создать ссылку
        </button>
      </div>

      {fresh ? (
        <div className="fresh-invite">
          <code>{fresh}</code>
          <button className="button ghost" type="button" onClick={() => void copy()}>
            {copied ? 'Скопировано' : 'Копировать'}
          </button>
          <p className="muted small">
            Ссылку видно только сейчас — сохраните её. Потом можно создать новую.
          </p>
        </div>
      ) : null}

      {invites.length > 0 ? (
        <ul className="invite-list">
          {invites.map((invite) => (
            <li key={invite.id} className="member-row">
              <div className="muted small">
                {invite.role === 'editor' ? 'редактор' : 'просмотр'} · до{' '}
                {new Date(invite.expiresAt).toLocaleDateString('ru-RU')} · переходов: {invite.uses}
                {invite.editDays ? ` · правки ${invite.editDays} дн.` : ''}
              </div>
              <button className="button ghost danger" type="button" onClick={() => void revoke(invite)}>
                Отозвать
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      <p className="muted small">
        По ссылке войдёт любой, у кого она есть. Право менять доску действует
        ограниченное время — дальше участник остаётся только наблюдателем.
      </p>

      {error ? <p className="error">{error}</p> : null}
    </div>
  );
}
