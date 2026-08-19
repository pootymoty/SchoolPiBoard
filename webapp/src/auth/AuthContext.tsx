import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { api, readToken, writeToken } from '../api/client';
import type { AuthResponse, Subscription, User } from '../api/types';

interface AuthState {
  user: User | null;
  subscription: Subscription | null;
  /** Пока true, ещё не известно, вошёл пользователь или нет. */
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, displayName: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [subscription, setSubscription] = useState<Subscription | null>(null);
  const [loading, setLoading] = useState(true);

  // При загрузке страницы токен уже может лежать в localStorage —
  // проверяем его у сервера, а не верим на слово.
  useEffect(() => {
    const token = readToken();
    if (!token) {
      setLoading(false);
      return;
    }

    let cancelled = false;

    api<{ user: User; subscription: Subscription | null }>('/auth/me')
      .then((result) => {
        if (cancelled) return;
        setUser(result.user);
        setSubscription(result.subscription);
      })
      .catch(() => {
        if (cancelled) return;
        writeToken(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const apply = useCallback((result: AuthResponse) => {
    writeToken(result.token);
    setUser(result.user);
    setSubscription(result.subscription);
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const result = await api<AuthResponse>('/auth/login', {
        method: 'POST',
        body: { email, password },
      });
      apply(result);
    },
    [apply],
  );

  const register = useCallback(
    async (email: string, password: string, displayName: string) => {
      const result = await api<AuthResponse>('/auth/register', {
        method: 'POST',
        body: { email, password, displayName },
      });
      apply(result);
    },
    [apply],
  );

  const logout = useCallback(() => {
    writeToken(null);
    setUser(null);
    setSubscription(null);
  }, []);

  const value = useMemo<AuthState>(
    () => ({ user, subscription, loading, login, register, logout }),
    [user, subscription, loading, login, register, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth вызван вне AuthProvider');
  }
  return context;
}
