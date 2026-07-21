import { describe, expect, it } from 'vitest';
import { render } from '@testing-library/react';
import { BerryIcon } from './BerryIcon';

describe('BerryIcon', () => {
  it('renders a themed icon for a known berry type', () => {
    const { container } = render(<BerryIcon berryType="Strawberries" />);
    expect(container.querySelector('svg.berry-icon')).not.toBeNull();
    expect(container.querySelector('path[fill="#e5384f"]')).not.toBeNull();
  });

  it('falls back to the generic icon for an unrecognized berry type', () => {
    const { container } = render(<BerryIcon berryType="Kiwi" />);
    expect(container.querySelector('svg.berry-icon')).not.toBeNull();
    expect(container.querySelector('circle[fill="var(--accent)"]')).not.toBeNull();
  });
});
