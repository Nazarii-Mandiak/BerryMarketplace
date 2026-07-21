import { Navigate, Route, Routes } from 'react-router-dom';
import { Layout } from './components/Layout';
import { RequireAuth } from './features/auth/RequireAuth';
import { LoginPage } from './features/auth/LoginPage';
import { RegisterPage } from './features/auth/RegisterPage';
import { MarketPage } from './features/market/MarketPage';
import { SellPage } from './features/sell/SellPage';
import { ReservationsPage } from './features/reservations/ReservationsPage';

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/market" replace />} />
        <Route path="/market" element={<MarketPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route element={<RequireAuth />}>
          <Route path="/sell" element={<SellPage />} />
          <Route path="/reservations" element={<ReservationsPage />} />
        </Route>
      </Route>
    </Routes>
  );
}
