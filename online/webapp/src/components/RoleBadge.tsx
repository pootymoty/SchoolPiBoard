import type { ReactElement } from 'react';
import type { BoardRole } from '../api/types';

const LABELS: Record<BoardRole, string> = {
  owner: 'владелец',
  editor: 'редактор',
  viewer: 'просмотр',
};

export function RoleBadge({ role }: { role: BoardRole }): ReactElement {
  return <span className={`badge badge-${role}`}>{LABELS[role]}</span>;
}
