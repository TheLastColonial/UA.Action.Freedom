import type { ZodType } from 'zod';

import { env } from '../env';
import {
  ApiDomainProblem,
  ApiError,
  ApiForbidden,
  ApiNetworkError,
  ApiNotFound,
  ApiUnauthorized,
  ApiValidationProblem,
  problemJsonSchema,
} from './problem';

export type ExpectKind = 'json' | 'created' | 'nocontent' | 'collection';

export type CreatedResource = { readonly id: string };
export type ParentMissing = { readonly parentMissing: true };

type QueryValue = string | number | boolean | undefined;

export interface ApiRequest<T> {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
  path: string;
  expect: ExpectKind;
  query?: Readonly<Record<string, QueryValue>>;
  body?: unknown;
  schema?: ZodType<T>;
  signal?: AbortSignal;
}

interface ApiClientConfig {
  baseUrl: string;
  getAccessToken: () => string | undefined;
  onUnauthorized: () => void | Promise<void>;
  onSlowRequest: () => void;
  slowRequestThresholdMs: number;
}

const defaults = (): ApiClientConfig => ({
  baseUrl: env.VITE_API_BASE_URL,
  getAccessToken: () => undefined,
  onUnauthorized: () => undefined,
  onSlowRequest: () => undefined,
  slowRequestThresholdMs: 3000,
});

let config: ApiClientConfig = defaults();

export function configureApiClient(next: Partial<ApiClientConfig>): void {
  config = { ...config, ...next };
}

/** Test seam — restore the module to its unconfigured state. */
export function resetApiClient(): void {
  config = defaults();
}

function buildUrl(path: string, query: ApiRequest<unknown>['query']): string {
  const url = `${config.baseUrl}${path}`;
  if (!query) {
    return url;
  }
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params.set(key, String(value));
    }
  }
  const qs = params.toString();
  return qs.length > 0 ? `${url}?${qs}` : url;
}

async function readProblem(response: Response): Promise<never> {
  const contentType = response.headers.get('content-type') ?? '';
  if (contentType.includes('problem+json') || contentType.includes('application/json')) {
    const parsed = problemJsonSchema.safeParse(await response.json().catch(() => null));
    if (parsed.success) {
      const problem = parsed.data;
      if (response.status === 400 && problem.errors) {
        throw new ApiValidationProblem(problem.errors, problem.detail);
      }
      throw new ApiDomainProblem(response.status, problem.title, problem.detail);
    }
  }
  throw new ApiError(`Request failed with status ${String(response.status)}`, response.status);
}

/**
 * One entry point for every API call. Adds the bearer token, applies the response
 * conventions the API commits to (Location on 201, empty body on 204, problem+json on 4xx,
 * 404-vs-`[]` for sub-resource collections) and never sets a client-side timeout — a cold
 * start can take a while and no request should be abandoned early.
 */
export function request<T>(req: ApiRequest<T> & { expect: 'json' }): Promise<T>;
export function request(req: ApiRequest<unknown> & { expect: 'created' }): Promise<CreatedResource>;
export function request(req: ApiRequest<unknown> & { expect: 'nocontent' }): Promise<void>;
export function request<T>(
  req: ApiRequest<T> & { expect: 'collection' },
): Promise<T | ParentMissing>;
export async function request<T>(
  req: ApiRequest<T>,
): Promise<T | CreatedResource | ParentMissing | void> {
  const headers = new Headers({ Accept: 'application/json' });
  const token = config.getAccessToken();
  if (token !== undefined) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const hasBody = req.body !== undefined && req.method !== 'GET';
  if (hasBody) {
    headers.set('Content-Type', 'application/json');
  }

  const slowTimer = setTimeout(() => {
    config.onSlowRequest();
  }, config.slowRequestThresholdMs);

  const init: RequestInit = { method: req.method, headers };
  if (hasBody) {
    init.body = JSON.stringify(req.body);
  }
  if (req.signal) {
    init.signal = req.signal;
  }

  let response: Response;
  try {
    response = await fetch(buildUrl(req.path, req.query), init);
  } catch (cause) {
    throw new ApiNetworkError(cause);
  } finally {
    clearTimeout(slowTimer);
  }

  if (response.status === 401) {
    await config.onUnauthorized();
    throw new ApiUnauthorized();
  }
  if (response.status === 403) {
    throw new ApiForbidden();
  }

  if (req.expect === 'collection') {
    if (response.status === 404) {
      return { parentMissing: true };
    }
    if (!response.ok) {
      return readProblem(response);
    }
    return parse(req.schema, await response.json());
  }

  if (!response.ok) {
    if (response.status === 404) {
      throw new ApiNotFound();
    }
    return readProblem(response);
  }

  if (req.expect === 'nocontent') {
    return;
  }

  if (req.expect === 'created') {
    const location = response.headers.get('location') ?? '';
    const id = decodeURIComponent(location.slice(location.lastIndexOf('/') + 1));
    return { id };
  }

  return parse(req.schema, await response.json());
}

function parse<T>(schema: ZodType<T> | undefined, value: unknown): T {
  return schema ? schema.parse(value) : (value as T);
}
