import { useState } from 'react';
import type { FormEvent, ReactElement } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Subscription } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Page } from '../components/Layout';

export function ProfilePage(): ReactElement {
  const { user, subscription, refresh, setSubscription } = useAuth();

  const [lastName, setLastName] = useState(user?.lastName ?? '');
  const [firstName, setFirstName] = useState(user?.firstName ?? '');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = async (action: () => Promise<string>) => {
    setBusy(true);
    setError(null);
    setNotice(null);

    try {
      setNotice(await action());
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не получилось. Попробуйте ещё раз.');
    } finally {
      setBusy(false);
    }
  };

  const saveName = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api('/profile', { method: 'PATCH', body: { lastName, firstName } });
      await refresh();
      return 'Имя сохранено.';
    });
  };

  const savePassword = (event: FormEvent) => {
    event.preventDefault();
    void run(async () => {
      await api('/profile/password', {
        method: 'POST',
        body: { currentPassword, newPassword, confirmPassword },
      });
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      return 'Пароль изменён. Мы отправили уведомление на почту.';
    });
  };

  const setAutoRenew = (enabled: boolean) => {
    void run(async () => {
      const result = await api<{ subscription: Subscription }>('/billing/auto-renew', {
        method: 'POST',
        body: { enabled },
      });
      setSubscription(result.subscription);
      return enabled ? 'Автопродление включено.' : 'Автопродление отключено.';
    });
  };

  const cancel = () => {
    if (!window.confirm('Отменить подписку? Доступ сохранится до конца оплаченного срока.')) return;

    void run(async () => {
      const result = await api<{ subscription: Subscription; message: string }>('/billing/cancel', { method: 'POST' });
      setSubscription(result.subscription);
      return result.message;
    });
  };

  const requestDeletion = () => {
    if (!window.confirm('Отправить письмо со ссылкой на удаление учётной записи?')) return;

    void run(async () => {
      const result = await api<{ message: string }>('/profile/delete-request', { method: 'POST' });
      return result.message;
    });
  };

  return (
    <Page>
      <h1>Профиль</h1>

      {notice ? <p className="banner">{notice}</p> : null}
      {error ? <p className="error">{error}</p> : null}

      <section className="card section-card">
        <h2>Подписка</h2>

        {subscription ? (
          <>
            <p>
              {subscription.kind === 'trial' ? 'Пробный период' : `Подписка на ${subscription.planDays} дн.`} ·{' '}
              {subscription.active ? 'действует' : 'закончилась'} до{' '}
              {new Date(subscription.expiresAt).toLocaleDateString('ru-RU')}
              {subscription.status === 'canceled' ? ' · отменена, продления не будет' : ''}
            </p>

            <div className="row">
              <Link className="button" to="/subscribe">Продлить</Link>

              {subscription.autoRenew ? (
                <button className="button ghost" type="button" disabled={busy} onClick={() => setAutoRenew(false)}>
                  Отключить автопродление
                </button>
              ) : (
                <button className="button ghost" type="button" disabled={busy} onClick={() => setAutoRenew(true)}>
                  Включить автопродление
                </button>
              )}

              {subscription.status !== 'canceled' ? (
                <button className="button ghost danger" type="button" disabled={busy} onClick={cancel}>
                  Отменить подписку
                </button>
              ) : null}
            </div>
          </>
        ) : (
          <p className="muted">
            Подписки нет. <Link to="/subscribe">Выбрать тариф или взять пробный период</Link>
          </p>
        )}
      </section>

      <section className="card section-card">
        <h2>Имя</h2>
        <form className="form-inline" onSubmit={saveName}>
          <label htmlFor="lastName">Фамилия</label>
          <input id="lastName" type="text" required value={lastName}
                 onChange={(event) => setLastName(event.target.value)} />

          <label htmlFor="firstName">Имя</label>
          <input id="firstName" type="text" required value={firstName}
                 onChange={(event) => setFirstName(event.target.value)} />

          <button className="button" type="submit" disabled={busy}>Сохранить</button>
        </form>
        <p className="muted small">Почта: {user?.email}</p>
      </section>

      <section className="card section-card">
        <h2>Пароль</h2>
        <form className="form-inline" onSubmit={savePassword}>
          <label htmlFor="currentPassword">Текущий пароль</label>
          <input id="currentPassword" type="password" required autoComplete="current-password"
                 value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} />

          <label htmlFor="newPassword">Новый пароль</label>
          <input id="newPassword" type="password" required minLength={8} autoComplete="new-password"
                 value={newPassword} onChange={(event) => setNewPassword(event.target.value)} />

          <label htmlFor="confirmPassword">Повторите новый пароль</label>
          <input id="confirmPassword" type="password" required autoComplete="new-password"
                 value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} />

          <button className="button" type="submit" disabled={busy}>Изменить пароль</button>
        </form>
      </section>

      <section className="card section-card danger-zone">
        <h2>Удаление учётной записи</h2>
        <p className="muted">
          Мы пришлём письмо со ссылкой. После подтверждения удалим учётную запись,
          ваши доски и отменим подписку — вернуть это будет нельзя.
        </p>
        <button className="button ghost danger" type="button" disabled={busy} onClick={requestDeletion}>
          Запросить удаление
        </button>
      </section>
    </Page>
  );
}
