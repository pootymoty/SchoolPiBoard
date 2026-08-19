import { useEffect, useState } from 'react';
import type { ReactElement } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { Plan, Subscription } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Page } from '../components/Layout';

interface PlansResponse {
  trialDays: number;
  plans: Plan[];
}

/** Выбор: пробный период или подписка. Показывается сразу после входа. */
export function SubscribePage(): ReactElement {
  const { user, subscription, setSubscription } = useAuth();
  const navigate = useNavigate();

  const [plans, setPlans] = useState<Plan[]>([]);
  const [trialDays, setTrialDays] = useState(7);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api<PlansResponse>('/billing/plans')
      .then((result) => {
        setPlans(result.plans);
        setTrialDays(result.trialDays);
      })
      .catch(() => setError('Не удалось загрузить тарифы.'));
  }, []);

  const startTrial = async () => {
    setBusy(true);
    setError(null);

    try {
      const result = await api<{ subscription: Subscription }>('/billing/trial', { method: 'POST' });
      setSubscription(result.subscription);
      navigate('/boards');
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось начать пробный период.');
    } finally {
      setBusy(false);
    }
  };

  const buy = async (days: number) => {
    setBusy(true);
    setError(null);

    try {
      const result = await api<{ paymentUrl: string }>('/billing/checkout', {
        method: 'POST',
        body: { planDays: days },
      });

      // Дальше человека ведёт платёжная система; об оплате сервер узнает
      // от неё самой, а не от браузера.
      window.location.href = result.paymentUrl;
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : 'Не удалось перейти к оплате.');
      setBusy(false);
    }
  };

  return (
    <Page>
      <h1>Подписка</h1>

      {subscription?.active ? (
        <p className="banner">
          Подписка действует до {new Date(subscription.expiresAt).toLocaleDateString('ru-RU')}.
          Оплата продлит её с этой даты, а не с сегодняшней.
        </p>
      ) : null}

      {!subscription && !user?.trialUsed ? (
        <div className="card trial-card">
          <div>
            <h2>{trialDays} дней бесплатно</h2>
            <p className="muted">Полный доступ, карта не нужна. Даётся один раз.</p>
          </div>
          <button className="button" type="button" disabled={busy} onClick={() => void startTrial()}>
            Начать пробный период
          </button>
        </div>
      ) : null}

      <div className="cards">
        {plans.map((plan) => (
          <div className="card price-card" key={plan.days}>
            <b>{plan.title}</b>
            <span className="price">{plan.price.toLocaleString('ru-RU')} ₽</span>
            <button className="button" type="button" disabled={busy} onClick={() => void buy(plan.days)}>
              Оплатить
            </button>
          </div>
        ))}
      </div>

      {error ? <p className="error">{error}</p> : null}

      <p className="muted small">
        Подписка нужна, чтобы создавать свои доски. Работать на чужой доске
        по приглашению можно и без неё.
      </p>

      <p className="muted small">
        <Link to="/boards">Перейти к доскам</Link>
      </p>
    </Page>
  );
}
