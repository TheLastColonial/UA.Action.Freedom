import { HttpResponse, delay, http } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { z } from 'zod';

import { worker } from '../test/msw/worker';
import { configureApiClient, request, resetApiClient } from './client';
import {
  ApiDomainProblem,
  ApiError,
  ApiForbidden,
  ApiUnauthorized,
  ApiValidationProblem,
} from './problem';

const widget = z.object({ id: z.number(), name: z.string() });

beforeEach(() => {
  resetApiClient();
  configureApiClient({ getAccessToken: () => 'test-token' });
});

afterEach(() => {
  resetApiClient();
});

describe('request', () => {
  it('sends the bearer token and parses a JSON body with the given schema', async () => {
    let seenAuth: string | null = null;
    worker.use(
      http.get('/widgets/1', ({ request: req }) => {
        seenAuth = req.headers.get('authorization');
        return HttpResponse.json({ id: 1, name: 'crate' });
      }),
    );

    const result = await request({
      method: 'GET',
      path: '/widgets/1',
      expect: 'json',
      schema: widget,
    });

    expect(seenAuth).toBe('Bearer test-token');
    expect(result).toEqual({ id: 1, name: 'crate' });
  });

  it('returns the new resource id from the Location header on 201', async () => {
    worker.use(
      http.post(
        '/widgets',
        () => new HttpResponse(null, { status: 201, headers: { Location: '/widgets/ABC%20123' } }),
      ),
    );

    const created = await request({
      method: 'POST',
      path: '/widgets',
      expect: 'created',
      body: {},
    });

    expect(created).toEqual({ id: 'ABC 123' });
  });

  it('resolves with no value on 204', async () => {
    worker.use(http.put('/widgets/1', () => new HttpResponse(null, { status: 204 })));

    await expect(
      request({ method: 'PUT', path: '/widgets/1', expect: 'nocontent', body: {} }),
    ).resolves.toBeUndefined();
  });

  it('maps a 400 problem+json errors map to ApiValidationProblem with PascalCase keys', async () => {
    worker.use(
      http.put('/convoys/1/route', () =>
        HttpResponse.json(
          {
            title: 'Validation failed',
            status: 400,
            errors: { 'Stops[0].Postcode': ['required'] },
          },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const error = await request({
      method: 'PUT',
      path: '/convoys/1/route',
      expect: 'nocontent',
      body: {},
    }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiValidationProblem);
    expect((error as ApiValidationProblem).errors).toEqual({ 'Stops[0].Postcode': ['required'] });
  });

  it('maps a 409 problem+json to ApiDomainProblem carrying the detail verbatim', async () => {
    worker.use(
      http.post('/convoys/1/publish-truck-list', () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            status: 409,
            detail: 'The truck list for this convoy has already been published.',
          },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const error = await request({
      method: 'POST',
      path: '/convoys/1/publish-truck-list',
      expect: 'nocontent',
    }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiDomainProblem);
    expect((error as ApiDomainProblem).status).toBe(409);
    expect((error as ApiDomainProblem).detail).toBe(
      'The truck list for this convoy has already been published.',
    );
  });

  it('distinguishes a missing parent (404) from an empty collection (200 [])', async () => {
    worker.use(
      http.get('/convoys/1/route', () => HttpResponse.json([])),
      http.get('/convoys/999/route', () => new HttpResponse(null, { status: 404 })),
    );

    const empty = await request({
      method: 'GET',
      path: '/convoys/1/route',
      expect: 'collection',
      schema: z.array(z.unknown()),
    });
    const missing = await request({
      method: 'GET',
      path: '/convoys/999/route',
      expect: 'collection',
      schema: z.array(z.unknown()),
    });

    expect(empty).toEqual([]);
    expect(missing).toEqual({ parentMissing: true });
  });

  it('calls onUnauthorized once on 401 and throws ApiUnauthorized without retrying', async () => {
    let calls = 0;
    worker.use(
      http.get('/widgets/1', () => {
        calls += 1;
        return new HttpResponse(null, { status: 401 });
      }),
    );
    const onUnauthorized = vi.fn();
    configureApiClient({ onUnauthorized });

    const error = await request({
      method: 'GET',
      path: '/widgets/1',
      expect: 'json',
      schema: widget,
    }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiUnauthorized);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);
    expect(calls).toBe(1);
  });

  it('throws ApiForbidden on 403', async () => {
    worker.use(http.get('/widgets/1', () => new HttpResponse(null, { status: 403 })));

    const error = await request({
      method: 'GET',
      path: '/widgets/1',
      expect: 'json',
      schema: widget,
    }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiForbidden);
  });

  it('throws a plain ApiError on an unexpected status', async () => {
    worker.use(http.get('/widgets/1', () => new HttpResponse(null, { status: 500 })));

    const error = await request({
      method: 'GET',
      path: '/widgets/1',
      expect: 'json',
      schema: widget,
    }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
  });

  it('does not impose a client-side timeout on a slow request', async () => {
    worker.use(
      http.get('/widgets/1', async () => {
        await delay(80);
        return HttpResponse.json({ id: 1, name: 'crate' });
      }),
    );

    const result = await request({
      method: 'GET',
      path: '/widgets/1',
      expect: 'json',
      schema: widget,
    });

    expect(result).toEqual({ id: 1, name: 'crate' });
  });

  it('fires the slow-request callback when a request outlives the threshold', async () => {
    const onSlowRequest = vi.fn();
    configureApiClient({ onSlowRequest, slowRequestThresholdMs: 20 });
    worker.use(
      http.get('/widgets/1', async () => {
        await delay(60);
        return HttpResponse.json({ id: 1, name: 'crate' });
      }),
    );

    await request({ method: 'GET', path: '/widgets/1', expect: 'json', schema: widget });

    expect(onSlowRequest).toHaveBeenCalled();
  });
});
