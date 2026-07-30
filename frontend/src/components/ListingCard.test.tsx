import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListingCard } from './ListingCard';

describe('ListingCard', () => {
  it('renders berry, farm, price, and the footer slot', () => {
    render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Cinderfield Orchard" pricePerKg={12.64}>
        <span>footer content</span>
      </ListingCard>,
    );

    expect(screen.getByRole('heading', { name: 'Blueberries' })).toBeInTheDocument();
    expect(screen.getByText('Cinderfield Orchard')).toBeInTheDocument();
    expect(screen.getByText('$12.64/kg')).toBeInTheDocument();
    expect(screen.getByText('footer content')).toBeInTheDocument();
  });

  it('wraps in a .card-glow container only when glow is set', () => {
    const { container: plain } = render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Farm" pricePerKg={1} />,
    );
    expect(plain.querySelector('.card-glow')).not.toBeInTheDocument();
    expect(plain.querySelector('.card')).toBeInTheDocument();

    const { container: glowing } = render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Farm" pricePerKg={1} glow />,
    );
    expect(glowing.querySelector('.card-glow')).toBeInTheDocument();
    expect(glowing.querySelector('.card-glow .card')).toBeInTheDocument();
  });

  it('omits the note and tasting-notes paragraphs when absent', () => {
    const { container } = render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Farm" pricePerKg={1} />,
    );
    expect(container.querySelector('.note')).not.toBeInTheDocument();
    expect(container.querySelector('.tasting-notes')).not.toBeInTheDocument();
  });

  it('shows the drawn berry icon when hasPhoto is false, and a photo img when true', () => {
    const { container: withoutPhoto } = render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Farm" pricePerKg={1} />,
    );
    expect(withoutPhoto.querySelector('.berry-icon')).toBeInTheDocument();
    expect(withoutPhoto.querySelector('.card-photo')).not.toBeInTheDocument();

    const { container: withPhoto } = render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Farm" pricePerKg={1} hasPhoto />,
    );
    expect(withPhoto.querySelector('.berry-icon')).not.toBeInTheDocument();
    const img = withPhoto.querySelector('.card-photo') as HTMLImageElement;
    expect(img).toBeInTheDocument();
    expect(img.src).toContain('/api/listings/l1/photo');
  });

  it('renders extraContent above the footer', () => {
    render(
      <ListingCard listingId="l1" berryType="Blueberries" farmName="Farm" pricePerKg={1} extraContent={<div>picker</div>}>
        <span>footer</span>
      </ListingCard>,
    );

    expect(screen.getByText('picker')).toBeInTheDocument();
  });
});
