import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useCurrentUser } from './useCurrentUser';

export function RequireAuth() {
  const { data: user, isLoading } = useCurrentUser();
  const location = useLocation();

  if (isLoading) {
    return <p>Loading…</p>;
  }

  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  return <Outlet />;
}
