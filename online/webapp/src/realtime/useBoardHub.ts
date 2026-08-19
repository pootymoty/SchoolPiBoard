import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { API_URL, readToken } from '../api/client';
import type { BoardJoined, Participant } from '../api/types';

export type HubStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'error';

interface BoardHubState {
  status: HubStatus;
  error: string | null;
  board: BoardJoined | null;
  participants: Participant[];
  /** Курсоры остальных участников. Пригодятся на этапе холста. */
  cursors: Record<string, { x: number; y: number }>;
  sendCursor: (x: number, y: number) => void;
}

interface CursorMessage {
  userId: string;
  x: number;
  y: number;
}

/** Курсор шлём не чаще ~20 раз в секунду: чаще браузеру незачем, а трафика вдвое больше. */
const CURSOR_INTERVAL_MS = 50;

export function useBoardHub(boardId: string | undefined): BoardHubState {
  const [status, setStatus] = useState<HubStatus>('connecting');
  const [error, setError] = useState<string | null>(null);
  const [board, setBoard] = useState<BoardJoined | null>(null);
  const [participants, setParticipants] = useState<Participant[]>([]);
  const [cursors, setCursors] = useState<Record<string, { x: number; y: number }>>({});

  const connectionRef = useRef<HubConnection | null>(null);
  const lastCursorSentAt = useRef(0);

  useEffect(() => {
    if (!boardId) return;

    let disposed = false;

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hub/board`, {
        // WebSocket не умеет слать заголовок Authorization, поэтому SignalR
        // добавляет токен в строку запроса — сервер принимает его только здесь.
        accessTokenFactory: () => readToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on('UserJoined', (participant: Participant) => {
      setParticipants((current) => {
        const others = current.filter((x) => x.userId !== participant.userId);
        return [...others, participant];
      });
    });

    connection.on('UserLeft', (userId: string) => {
      setParticipants((current) => current.filter((x) => x.userId !== userId));
      setCursors((current) => {
        const next = { ...current };
        delete next[userId];
        return next;
      });
    });

    connection.on('CursorMoved', (cursor: CursorMessage) => {
      setCursors((current) => ({ ...current, [cursor.userId]: { x: cursor.x, y: cursor.y } }));
    });

    // Переподключение: заново входим в комнату и получаем состояние целиком,
    // а не пытаемся догнать пропущенные события.
    const join = async () => {
      const joined = await connection.invoke<BoardJoined>('JoinBoard', boardId);
      if (disposed) return;
      setBoard(joined);
      setParticipants(joined.participants);
      setStatus('connected');
      setError(null);
    };

    connection.onreconnecting(() => {
      if (!disposed) setStatus('reconnecting');
    });

    connection.onreconnected(() => {
      if (disposed) return;
      setStatus('connecting');
      join().catch((reason: unknown) => {
        if (disposed) return;
        setStatus('error');
        setError(describe(reason));
      });
    });

    connection.onclose(() => {
      if (!disposed) setStatus('disconnected');
    });

    setStatus('connecting');
    connection
      .start()
      .then(join)
      .catch((reason: unknown) => {
        if (disposed) return;
        setStatus('error');
        setError(describe(reason));
      });

    return () => {
      disposed = true;
      connectionRef.current = null;
      void connection.stop();
    };
  }, [boardId]);

  const sendCursor = useCallback(
    (x: number, y: number) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected || !boardId) return;

      const now = Date.now();
      if (now - lastCursorSentAt.current < CURSOR_INTERVAL_MS) return;
      lastCursorSentAt.current = now;

      void connection.send('CursorMove', boardId, x, y);
    },
    [boardId],
  );

  return { status, error, board, participants, cursors, sendCursor };
}

function describe(reason: unknown): string {
  if (reason instanceof Error && reason.message) {
    // HubException доезжает сюда текстом, который написан на сервере.
    return reason.message.replace(/^.*HubException:\s*/, '');
  }
  return 'Не удалось подключиться к доске.';
}
