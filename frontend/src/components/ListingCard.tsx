import type { ReactNode } from 'react';
import { BerryIcon } from './BerryIcon';
import { GlowingEffect } from './ui/glowing-effect';

const FINE_POINTER = typeof matchMedia === 'function' && matchMedia('(pointer: fine)').matches;

function formatPrice(pricePerPint: number): string {
  return `$${pricePerPint.toFixed(2)}/pt`;
}

interface ListingCardProps {
  berryType: string;
  farmName: string;
  pricePerPint: number;
  note?: string | null;
  aiTastingNotes?: string | null;
  glow?: boolean;
  children?: ReactNode;
}

export function ListingCard({
  berryType,
  farmName,
  pricePerPint,
  note,
  aiTastingNotes,
  glow = false,
  children,
}: ListingCardProps) {
  const card = (
    <div className="card">
      <div className="art">
        <BerryIcon berryType={berryType} />
        <span className="price-tag">{formatPrice(pricePerPint)}</span>
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
