import { Link } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCurrentUser, CURRENT_USER_QUERY_KEY } from '../features/auth/useCurrentUser';
import { logout } from '../api/accounts';

export function Header() {
  const { data: user } = useCurrentUser();
  const queryClient = useQueryClient();
  const logoutMutation = useMutation({
    mutationFn: logout,
    onSuccess: () => {
      queryClient.setQueryData(CURRENT_USER_QUERY_KEY, null);
    },
  });

  return (
    <header className="site-header">
      <Link to="/market" className="wordmark">
        Berrow
      </Link>
      <nav className="site-nav">
        <Link to="/market">The Market</Link>
        <Link to="/sell">Sell Berries</Link>
        <Link to="/reservations">My Reservations</Link>
      </nav>
      {user ? (
        <div className="auth-status">
          <span>{user.displayName}</span>
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => logoutMutation.mutate()}
            disabled={logoutMutation.isPending}
          >
            Log out
          </button>
        </div>
      ) : (
        <Link to="/login" className="btn btn-ghost">
          Log in
        </Link>
      )}
    </header>
  );
}
