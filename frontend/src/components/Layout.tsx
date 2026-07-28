import { Outlet } from 'react-router-dom';
import { Header } from './Header';
import { Footer } from './Footer';
import { ChatWidget } from '../features/chat/ChatWidget';
import { useCurrentUser } from '../features/auth/useCurrentUser';

export function Layout() {
  const { data: user } = useCurrentUser();

  return (
    <>
      <Header />
      <main>
        <Outlet />
      </main>
      <Footer />
      <ChatWidget isAuthenticated={!!user} />
    </>
  );
}
