import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListingCard } from './ListingCard';

describe('ListingCard', () => {
  it('renders berry, farm, price, and the footer slot', () => {
    render(
      <ListingCard berryType="Blueberries" farmName="Cinderfield Orchard" pricePerPint={12.64}>
        <span>footer content</span>
      </ListingCard>,
    );

    expect(screen.getByRole('heading', { name: 'Blueberries' })).toBeInTheDocument();
    expect(screen.getByText('Cinderfield Orchard')).toBeInTheDocument();
    expect(screen.getByText('$12.64/pt')).toBeInTheDocument();
    expect(screen.getByText('footer content')).toBeInTheDocument();
  });

  it('wraps in a .card-glow container only when glow is set', () => {
    const { container: plain } = render(<ListingCard berryType="Blueberries" farmName="Farm" pricePerPint={1} />);
    expect(plain.querySelector('.card-glow')).not.toBeInTheDocument();
    expect(plain.querySelector('.card')).toBeInTheDocument();

    const { container: glowing } = render(
      <ListingCard berryType="Blueberries" farmName="Farm" pricePerPint={1} glow />,
    );
    expect(glowing.querySelector('.card-glow')).toBeInTheDocument();
    expect(glowing.querySelector('.card-glow .card')).toBeInTheDocument();
  });

  it('omits the note and tasting-notes paragraphs when absent', () => {
    const { container } = render(<ListingCard berryType="Blueberries" farmName="Farm" pricePerPint={1} />);
    expect(container.querySelector('.note')).not.toBeInTheDocument();
    expect(container.querySelector('.tasting-notes')).not.toBeInTheDocument();
  });
});
