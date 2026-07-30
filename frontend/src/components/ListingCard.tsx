import type { ReactNode } from 'react';
import { BerryIcon } from './BerryIcon';
import { GlowingEffect } from './ui/glowing-effect';

const FINE_POINTER = typeof matchMedia === 'function' && matchMedia('(pointer: fine)').matches;

export function formatPrice(pricePerKg: number): string {
  return `$${pricePerKg.toFixed(2)}/kg`;
}

interface ListingCardProps {
  listingId: string;
  berryType: string;
  farmName: string;
  pricePerKg: number;
  hasPhoto?: boolean;
  note?: string | null;
  aiTastingNotes?: string | null;
  glow?: boolean;
  /** Rendered in card-body, above the footer — for content that doesn't fit the
   * footer's single space-between row (e.g. MarketPage's quantity picker). */
  extraContent?: ReactNode;
  children?: ReactNode;
}

export function ListingCard({
  listingId,
  berryType,
  farmName,
  pricePerKg,
  hasPhoto = false,
  note,
  aiTastingNotes,
  glow = false,
  extraContent,
  children,
}: ListingCardProps) {
  const card = (
    <div className="card">
      <div className="art">
        {hasPhoto ? (
          <img src={`/api/listings/${listingId}/photo`} alt="" loading="lazy" className="card-photo" />
        ) : (
          <BerryIcon berryType={berryType} />
        )}
        <span className="price-tag">{formatPrice(pricePerKg)}</span>
      </div>
      <div className="card-body">
        <h3>{berryType}</h3>
        <span className="farm">{farmName}</span>
        {note && <p className="note">{note}</p>}
        {aiTastingNotes && (
          <p className="tasting-notes">
            <em>{aiTastingNotes}</em>
          </p>
        )}
        {extraContent}
        <div className="card-foot">{children}</div>
      </div>
    </div>
  );

  if (!glow) {
    return card;
  }

  return (
    <div className="card-glow">
      <GlowingEffect
        spread={40}
        proximity={64}
        inactiveZone={0.01}
        borderWidth={3}
        blur={0}
        glow
        disabled={!FINE_POINTER}
      />
      {card}
    </div>
  );
}
