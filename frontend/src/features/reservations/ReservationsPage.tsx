import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getMyReservations } from '../../api/reservations';
import { ListingCard } from '../../components/ListingCard';
import type { ReservationWithListingResponse } from '../../api/types';

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

export function ReservationsPage() {
  const { data: reservations, isLoading } = useQuery<ReservationWithListingResponse[]>({
    queryKey: ['reservations', 'mine'],
    queryFn: getMyReservations,
  });

  return (
    <section className="reservations">
      <h2>My Reservations</h2>
      {isLoading && <p className="empty-state">Loading your reservations…</p>}
      {!isLoading && (reservations?.length ?? 0) === 0 && (
        <p className="empty-state">
          No reservations yet — the market's <Link to="/market">this way</Link>.
        </p>
      )}
      <div className="grid">
        {reservations?.map((reservation) => (
          <ListingCard
            key={reservation.id}
            berryType={reservation.berryType}
            farmName={reservation.farmName}
            pricePerPint={reservation.pricePerPint}
          >
            <span className="status-badge">{reservation.status}</span>
            <span className="qty">{formatDate(reservation.reservedAt)}</span>
          </ListingCard>
        ))}
      </div>
    </section>
  );
}
