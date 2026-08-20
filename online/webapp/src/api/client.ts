export const API_URL: string = (import.meta.env.VITE_API_URL as string | undefined) ?? 'http://localhost:5000';

const TOKEN_KEY = 'schoolpiboard.token';

export function readToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function writeToken(token: string | null): void {
  if (token) {
    localStorage.setItem(TOKEN_KEY, token);
  } else {
    localStorage.removeItem(TOKEN_KEY);
  }
}

/**
 * Ошибка от сервера в том виде, в каком её можно показать человеку.
 * Сервер всегда присылает message на русском — придумывать свой текст
 * поверх него не нужно.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const token = readToken();

  const headers: Record<string, string> = {};
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  let response: Response;
  try {
    response = await fetch(`${API_URL}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
    });
  } catch {
    // Сеть не ответила вовсе — отличаем это от ошибки сервера.
    throw new ApiError(0, 'network', 'Сервер не отвечает. Проверьте подключение.');
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const payload: unknown = text ? safeParse(text) : null;

  if (!response.ok) {
    const details = (payload ?? {}) as { error?: string; message?: string };
    throw new ApiError(
      response.status,
      details.error ?? 'error',
      details.message ?? defaultMessage(response.status),
    );
  }

  return payload as T;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function defaultMessage(status: number): string {
  if (status === 401) return 'Нужно войти заново.';
  if (status === 403) return 'Недостаточно прав.';
  if (status === 404) return 'Не найдено.';
  if (status === 429) return 'Слишком много попыток. Подождите минуту.';
  return 'Что-то пошло не так. Попробуйте ещё раз.';
}
