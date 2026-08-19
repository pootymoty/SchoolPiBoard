import type { ReactElement } from 'react';

interface Props {
  page: number;
  pageSize: number;
  total: number;
  onChange: (page: number) => void;
}

/** Постраничный переход. При одной странице не показывается вовсе. */
export function Pagination({ page, pageSize, total, onChange }: Props): ReactElement | null {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages <= 1) return null;

  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, total);

  return (
    <div className="pagination">
      <button className="button ghost" type="button" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Назад
      </button>

      <span className="muted small">
        Показано {from}–{to} из {total}
      </span>

      <button className="button ghost" type="button" disabled={page >= pages} onClick={() => onChange(page + 1)}>
        Вперёд
      </button>
    </div>
  );
}
