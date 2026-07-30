export class ApiError extends Error {
  status: number;
  errors: string[];

  constructor(status: number, errors: string[]) {
    super(errors.join(' '));
    this.status = status;
    this.errors = errors;
  }
}

async function parseErrorBody(response: Response): Promise<string[]> {
  try {
    const body = await response.json();
    if (Array.isArray(body?.errors)) return body.errors;
    if (typeof body?.error === 'string') return [body.error];
  } catch {
    // no JSON body
  }
  return [response.statusText || `Request failed with status ${response.status}`];
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  // FormData bodies (photo uploads) must not get a manual Content-Type: the browser sets
  // the multipart boundary itself, and overriding it here would break the upload.
  const isFormData = init?.body instanceof FormData;
  const response = await fetch(`/api${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      ...(init?.body && !isFormData ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  });

  if (!response.ok) {
    throw new ApiError(response.status, await parseErrorBody(response));
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }
  return JSON.parse(text) as T;
}
