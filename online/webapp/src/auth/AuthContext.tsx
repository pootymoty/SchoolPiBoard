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
  logout: () => void;
  /** Перечитать пользователя и подписку — после оплаты или смены имени. */
  refresh: () => Promise<void>;
  setSubscription: (subscription: Subscription | null) => void;
}

interface MeResponse {
  user: User;
  subscription: Subscription | null;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [subscription, setSubscription] = useState<Subscription | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    const result = await api<MeResponse>('/auth/me');
    setUser(result.user);
    setSubscription(result.subscription);
  }, []);

  // Токен из localStorage проверяем у сервера, а не верим ему на слово.
  useEffect(() => {
    if (!readToken()) {
      setLoading(false);
      return;
    }

    let cancelled = false;

    refresh()
      .catch(() => {
        if (!cancelled) writeToken(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [refresh]);

  const login = useCallback(async (email: string, password: string) => {
    const result = await api<AuthResponse>('/auth/login', {
      method: 'POST',
      body: { email, password },
    });

    writeToken(result.token);
    setUser(result.user);
    setSubscription(result.subscription);
  }, []);

  const logout = useCallback(() => {
    writeToken(null);
    setUser(null);
    setSubscription(null);
  }, []);

  const value = useMemo<AuthState>(
    () => ({ user, subscription, loading, login, logout, refresh, setSubscription }),
    [user, subscription, loading, login, logout, refresh],
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
