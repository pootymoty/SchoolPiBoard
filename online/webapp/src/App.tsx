import { Navigate, Route, Routes } from 'react-router-dom';
import type { ReactElement } from 'react';
import { useAuth } from './auth/AuthContext';
import { LandingPage } from './pages/LandingPage';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { ConfirmPage } from './pages/ConfirmPage';
import { SubscribePage } from './pages/SubscribePage';
import { BoardsPage } from './pages/BoardsPage';
import { BoardPage } from './pages/BoardPage';
import { ProfilePage } from './pages/ProfilePage';
import { DeleteAccountPage } from './pages/DeleteAccountPage';
import { JoinPage } from './pages/JoinPage';
import { LegalPage } from './pages/LegalPage';

export function App(): ReactElement {
  const { user, loading } = useAuth();

  if (loading) {
    return <div className="screen-center muted">Загружаем…</div>;
  }

  return (
    <Routes>
      {/* Открытые страницы: доступны и до входа. */}
      <Route path="/legal/:page" element={<LegalPage />} />
      <Route path="/confirm" element={<ConfirmPage />} />
      <Route path="/profile/delete" element={<DeleteAccountPage />} />
      <Route path="/join/:token" element={<JoinPage />} />

      {user ? (
        <>
          <Route path="/" element={<Navigate to="/boards" replace />} />
          <Route path="/login" element={<Navigate to="/boards" replace />} />
          <Route path="/register" element={<Navigate to="/boards" replace />} />
          <Route path="/boards" element={<BoardsPage />} />
          <Route path="/boards/:boardId" element={<BoardPage />} />
          <Route path="/subscribe" element={<SubscribePage />} />
          <Route path="/profile" element={<ProfilePage />} />
          <Route path="*" element={<Navigate to="/boards" replace />} />
        </>
      ) : (
        <>
          <Route path="/" element={<LandingPage />} />
          <Route path="/login" element={<LoginPage />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </>
      )}
    </Routes>
  );
}
