import { useMemo, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { deleteListing, getListings, reserveListing, searchListings } from '../../api/listings';
import { ApiError } from '../../api/client';
import { useCurrentUser } from '../auth/useCurrentUser';
import { useToast } from '../../components/ToastProvider';
import { BerryIcon } from '../../components/BerryIcon';
import { ListingCard } from '../../components/ListingCard';
import type { ListingResponse, SearchListingsResponse } from '../../api/types';

const LISTINGS_QUERY_KEY = ['listings'];
const HARVEST_BERRIES = ['Strawberries', 'Blueberries', 'Raspberries', 'Blackberries', 'Gooseberries'];
const QUANTITY_STEP_KG = 0.25;

function clampQuantity(value: number, maxKg: number): number {
  return Math.min(Math.max(value, 0.01), maxKg);
}

function QuantityPicker({ maxKg, quantity, onChange }: { maxKg: number; quantity: string; onChange: (next: string) => void }) {
  function nudge(delta: number) {
    const current = Number(quantity) || 0;
    const stepped = Math.round((current + delta) / QUANTITY_STEP_KG) * QUANTITY_STEP_KG;
    onChange(clampQuantity(stepped, maxKg).toFixed(2));
  }

  function handleBlur() {
    const value = Number(quantity);
    if (!Number.isFinite(value) || value <= 0) {
      onChange('0.01');
      return;
    }
    onChange(clampQuantity(Math.round(value * 100) / 100, maxKg).toFixed(2));
  }

  return (
    <div className="qty-picker">
      <button type="button" className="qty-picker-btn" onClick={() => nudge(-QUANTITY_STEP_KG)} aria-label="Decrease quantity">
        −
      </button>
      <input
        type="number"
        step="0.01"
        min="0.01"
        max={maxKg}
        value={quantity}
        onChange={(e) => onChange(e.target.value)}
        onBlur={handleBlur}
        aria-label="Quantity in kilograms"
      />
      <button type="button" className="qty-picker-btn" onClick={() => nudge(QUANTITY_STEP_KG)} aria-label="Increase quantity">
        +
      </button>
      <span className="qty-picker-unit">kg</span>
    </div>
  );
}

export function MarketPage() {
  const { data: user } = useCurrentUser();
  const { data: listings, isLoading, isError } = useQuery<ListingResponse[]>({
    queryKey: LISTINGS_QUERY_KEY,
    queryFn: getListings,
  });
  const queryClient = useQueryClient();
  const { showToast } = useToast();
  const navigate = useNavigate();
  const location = useLocation();
  const [activeType, setActiveType] = useState('all');
  const [search, setSearch] = useState('');
  const [smartSearch, setSmartSearch] = useState<SearchListingsResponse | null>(null);
  const [quantities, setQuantities] = useState<Record<string, string>>({});

  function quantityFor(listing: ListingResponse): string {
    return quantities[listing.id] ?? Math.min(0.5, listing.quantityAvailableKg || 0.5).toFixed(2);
  }

  const reserveMutation = useMutation({
    mutationFn: ({ listingId, quantityKg }: { listingId: string; quantityKg: number }) =>
      reserveListing(listingId, quantityKg),
    onMutate: async ({ listingId, quantityKg }) => {
      await queryClient.cancelQueries({ queryKey: LISTINGS_QUERY_KEY });
      const previous = queryClient.getQueryData<ListingResponse[]>(LISTINGS_QUERY_KEY);
      queryClient.setQueryData<ListingResponse[]>(LISTINGS_QUERY_KEY, (current) =>
        current?.map((listing) =>
          listing.id === listingId
            ? { ...listing, quantityAvailableKg: listing.quantityAvailableKg - quantityKg }
            : listing,
        ),
      );
      return { previous };
    },
    onError: (err, _vars, context) => {
      if (context?.previous) {
        queryClient.setQueryData(LISTINGS_QUERY_KEY, context.previous);
      }
      if (err instanceof ApiError && err.status === 401) {
        navigate('/login', { state: { from: location } });
        return;
      }
      showToast(err instanceof ApiError && err.status === 409 ? 'Sold out.' : 'Something went wrong — try again.');
    },
    onSuccess: (_data, { listingId, quantityKg }) => {
      const listing = listings?.find((l) => l.id === listingId);
      if (listing) {
        showToast(`Added ${quantityKg} kg of ${listing.berryType.toLowerCase()} to your reservations.`);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (listingId: string) => deleteListing(listingId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
      showToast('Listing deleted.');
    },
    onError: () => {
      showToast('Could not delete the listing — try again.');
    },
  });

  function handleDelete(listing: ListingResponse) {
    if (!window.confirm(`Delete this ${listing.berryType.toLowerCase()} listing? This can't be undone.`)) {
      return;
    }
    deleteMutation.mutate(listing.id);
  }

  const types = useMemo(() => {
    const seen = new Set<string>();
    const out: string[] = [];
    (listings ?? []).forEach((listing) => {
      if (!seen.has(listing.berryType)) {
        seen.add(listing.berryType);
        out.push(listing.berryType);
      }
    });
    return out;
  }, [listings]);

  const filtered = useMemo(() => {
    let items = listings ?? [];
    if (activeType !== 'all') {
      items = items.filter((listing) => listing.berryType === activeType);
    }
    if (search.trim()) {
      const term = search.trim().toLowerCase();
      items = items.filter(
        (listing) =>
          listing.berryType.toLowerCase().includes(term) || listing.farmName.toLowerCase().includes(term),
      );
    }
    return items;
  }, [listings, activeType, search]);

  async function runSmartSearch() {
    const q = search.trim();
    if (!q) return;
    try {
      setSmartSearch(await searchListings(q));
    } catch {
      showToast('Smart search failed — try again.');
    }
  }

  function renderCard(listing: ListingResponse) {
    const soldOut = listing.quantityAvailableKg <= 0;
    const low = !soldOut && listing.quantityAvailableKg <= 2;
    const isOwnListing = user?.id === listing.sellerId;
    const quantity = quantityFor(listing);
    const isReservingThis = reserveMutation.isPending && reserveMutation.variables?.listingId === listing.id;

    return (
      <ListingCard
        key={listing.id}
        listingId={listing.id}
        berryType={listing.berryType}
        farmName={listing.farmName}
        pricePerKg={listing.pricePerKg}
        hasPhoto={listing.hasPhoto}
        note={listing.note}
        aiTastingNotes={listing.aiTastingNotes}
        glow
        extraContent={
          !isOwnListing && !soldOut ? (
            <QuantityPicker
              maxKg={listing.quantityAvailableKg}
              quantity={quantity}
              onChange={(next) => setQuantities((prev) => ({ ...prev, [listing.id]: next }))}
            />
          ) : undefined
        }
      >
        <span className={`qty${low ? ' low' : ''}`}>
          {soldOut ? 'Sold out' : `${listing.quantityAvailableKg} kg left`}
        </span>
        {isOwnListing ? (
          <div className="own-listing-actions">
            <Link to={`/sell/${listing.id}`} className="btn-edit">
              Edit
            </Link>
            <button type="button" className="btn-delete" onClick={() => handleDelete(listing)}>
              Delete
            </button>
          </div>
        ) : (
          <button
            type="button"
            className="btn-buy"
            disabled={soldOut || isReservingThis}
            onClick={() => reserveMutation.mutate({ listingId: listing.id, quantityKg: Number(quantity) })}
          >
            {soldOut ? 'Sold out' : `Buy ${quantity} kg`}
          </button>
        )}
      </ListingCard>
    );
  }

  return (
    <>
      <section className="hero">
        <div className="hero-copy-left">
          <p className="eyebrow">Sunrow Valley</p>
          <h1>Berries, straight from the row.</h1>
        </div>
        <div className="hero-copy-right">
          <p className="lede">
            Berrow connects backyard growers and small orchards directly with buyers nearby — no
            middleman, no cold-chain trucking, just crates changing hands the same day they're picked.
          </p>
        </div>
      </section>

      <div className="harvest-banner">
        <span className="harvest-tag">Today's harvest</span>
        <div className="harvest-row">
          {HARVEST_BERRIES.map((berry) => (
            <BerryIcon key={berry} berryType={berry} />
          ))}
        </div>
      </div>

      <section className="market">
        <div className="market-head">
          <h2>The Market</h2>
          <span className="status">Fresh listings updated live</span>
        </div>
        <div className="filter-row">
          <div className="chips">
            <button
              type="button"
              className={`chip${activeType === 'all' ? ' active' : ''}`}
              onClick={() => setActiveType('all')}
            >
              All
            </button>
            {types.map((type) => (
              <button
                key={type}
                type="button"
                className={`chip${activeType === type ? ' active' : ''}`}
                onClick={() => setActiveType(type)}
              >
                {type}
              </button>
            ))}
          </div>
          <input
            className="search-input"
            type="search"
            placeholder="Search berries, farms…"
            aria-label="Search listings"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <button type="button" className="btn-smart-search" onClick={runSmartSearch}>
            Smart search
          </button>
        </div>
        {smartSearch && (
          <div className="smart-search-banner">
            <span className="badge">Smart results · {smartSearch.mode}</span>
            <button type="button" className="btn-clear" onClick={() => setSmartSearch(null)}>
              Clear
            </button>
          </div>
        )}
        {smartSearch ? (
          <div className="grid">
            {smartSearch.results.length === 0 && <p className="empty-state">No crates match that search.</p>}
            {smartSearch.results.map((listing) => renderCard(listing))}
          </div>
        ) : (
          <div className="grid">
            {isLoading && <p className="empty-state">Loading the market…</p>}
            {!isLoading && isError && (
              <p className="empty-state">Couldn't load the market — check your connection and try again.</p>
            )}
            {!isLoading && !isError && filtered.length === 0 && (
              <p className="empty-state">No crates match that search.</p>
            )}
            {filtered.map((listing) => renderCard(listing))}
          </div>
        )}
      </section>
    </>
  );
}
