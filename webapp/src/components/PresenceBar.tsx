import type { ReactElement } from 'react';
import type { Participant } from '../api/types';

/** Кто сейчас в доске. Цвет тот же, что у курсора участника на холсте. */
export function PresenceBar({ participants }: { participants: Participant[] }): ReactElement {
  if (participants.length === 0) {
    return <span className="muted small">Никого нет</span>;
  }

  return (
    <div className="presence">
      {participants.map((participant) => (
        <span
          key={participant.userId}
          className="avatar"
          style={{ backgroundColor: participant.color }}
          title={`${participant.name} — ${participant.role}`}
        >
          {initial(participant.name)}
        </span>
      ))}
    </div>
  );
}

function initial(name: string): string {
  const trimmed = name.trim();
  return trimmed.length > 0 ? trimmed[0].toUpperCase() : '?';
}
