import { useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getListings, reserveListing, searchListings } from '../../api/listings';
import { ApiError } from '../../api/client';
import { useCurrentUser } from '../auth/useCurrentUser';
import { useToast } from '../../components/ToastProvider';
import { BerryIcon } from '../../components/BerryIcon';
import type { ListingResponse, SearchListingsResponse } from '../../api/types';

const LISTINGS_QUERY_KEY = ['listings'];
const HARVEST_BERRIES = ['Strawberries', 'Blueberries', 'Raspberries', 'Blackberries', 'Gooseberries'];

function formatPrice(price: number): string {
  return `$${price.toFixed(2)}/pt`;
}

export function MarketPage() {
  const { data: user } = useCurrentUser();
  const { data: listings, isLoading } = useQuery<ListingResponse[]>({
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

  const reserveMutation = useMutation({
    mutationFn: (listingId: string) => reserveListing(listingId),
    onMutate: async (listingId: string) => {
      await queryClient.cancelQueries({ queryKey: LISTINGS_QUERY_KEY });
      const previous = queryClient.getQueryData<ListingResponse[]>(LISTINGS_QUERY_KEY);
      queryClient.setQueryData<ListingResponse[]>(LISTINGS_QUERY_KEY, (current) =>
        current?.map((listing) =>
          listing.id === listingId
            ? { ...listing, quantityAvailable: listing.quantityAvailable - 1 }
            : listing,
        ),
      );
      return { previous };
    },
    onError: (err, _listingId, context) => {
      if (context?.previous) {
        queryClient.setQueryData(LISTINGS_QUERY_KEY, context.previous);
      }
      if (err instanceof ApiError && err.status === 401) {
        navigate('/login', { state: { from: location } });
        return;
      }
      showToast(err instanceof ApiError && err.status === 409 ? 'Sold out.' : 'Something went wrong — try again.');
    },
    onSuccess: (_data, listingId) => {
      const listing = listings?.find((l) => l.id === listingId);
      if (listing) {
        showToast(`Added a pint of ${listing.berryType.toLowerCase()} to your reservations.`);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: LISTINGS_QUERY_KEY });
    },
  });

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
    setSmartSearch(await searchListings(q));
  }

  function renderCard(listing: ListingResponse) {
    const soldOut = listing.quantityAvailable <= 0;
    const low = !soldOut && listing.quantityAvailable <= 5;
    const isOwnListing = user?.id === listing.sellerId;
    return (
      <div className="card" key={listing.id}>
        <div className="art">
          <BerryIcon berryType={listing.berryType} />
          <span className="price-tag">{formatPrice(listing.pricePerPint)}</span>
        </div>
        <div className="card-body">
          <h3>{listing.berryType}</h3>
          <span className="farm">{listing.farmName}</span>
          {listing.note && <p className="note">{listing.note}</p>}
          {listing.aiTastingNotes && (
            <p className="tasting-notes">
              <em>{listing.aiTastingNotes}</em>
            </p>
          )}
          <div className="card-foot">
            <span className={`qty${low ? ' low' : ''}`}>
              {soldOut ? 'Sold out' : `${listing.quantityAvailable} pt${listing.quantityAvailable === 1 ? '' : 's'} left`}
            </span>
            {!isOwnListing && (
              <button
                type="button"
                className="btn-buy"
                disabled={soldOut || reserveMutation.isPending}
                onClick={() => reserveMutation.mutate(listing.id)}
              >
                {soldOut ? 'Sold out' : 'Buy a pint'}
              </button>
            )}
          </div>
        </div>
      </div>
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
            {!isLoading && filtered.length === 0 && <p className="empty-state">No crates match that search.</p>}
            {filtered.map((listing) => renderCard(listing))}
          </div>
        )}
      </section>
    </>
  );
}
