import { Link, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCurrentUser } from '../features/auth/useCurrentUser';
import { logout } from '../api/accounts';
import { useToast } from './ToastProvider';
import { ThemeToggle } from './ThemeToggle';

export function Header() {
  const { data: user } = useCurrentUser();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { showToast } = useToast();
  const logoutMutation = useMutation({
    mutationFn: logout,
    onSuccess: () => {
      queryClient.clear();
      navigate('/market');
    },
    onError: () => {
      showToast('Log out failed — try again.');
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
      <div className="auth-status">
        <ThemeToggle />
        {user ? (
          <>
            <span>{user.displayName}</span>
            <button
              type="button"
              className="btn btn-ghost"
              onClick={() => logoutMutation.mutate()}
              disabled={logoutMutation.isPending}
            >
              Log out
            </button>
          </>
        ) : (
          <Link to="/login" className="btn btn-ghost">
            Log in
          </Link>
        )}
      </div>
    </header>
  );
}
