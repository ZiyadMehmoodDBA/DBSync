import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from './useAuth';

export function AuthGuard() {
  const { accessToken } = useAuth();
  if (accessToken === null) {
    return <Navigate to="/login" replace />;
  }
  return <Outlet />;
}
