import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, ApiError } from './client';

describe('apiRequest', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('sends credentials and a JSON content-type when a body is present', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    const result = await apiRequest<{ ok: boolean }>('/thing', {
      method: 'POST',
      body: JSON.stringify({ a: 1 }),
    });

    expect(result).toEqual({ ok: true });
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/thing');
    expect(init.credentials).toBe('include');
    expect(init.headers['Content-Type']).toBe('application/json');
  });

  it('throws ApiError with parsed errors on a non-2xx JSON error body', async () => {
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(
        new Response(JSON.stringify({ errors: ['BerryType is required.'] }), { status: 400 }),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiRequest('/listings', { method: 'POST', body: '{}' })).rejects.toBeInstanceOf(ApiError);
    await expect(apiRequest('/listings', { method: 'POST', body: '{}' })).rejects.toMatchObject({
      status: 400,
      errors: ['BerryType is required.'],
    });
  });

  it('returns undefined for a 204 No Content response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })));

    const result = await apiRequest('/accounts/logout', { method: 'POST' });

    expect(result).toBeUndefined();
  });

  it('returns undefined for a 200 response with an empty body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 200 })));

    const result = await apiRequest('/accounts/logout', { method: 'POST' });

    expect(result).toBeUndefined();
  });
});
