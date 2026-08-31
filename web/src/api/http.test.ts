import { HttpResponse, http } from 'msw';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { z } from 'zod';

import { worker } from '../test/msw/worker';
import { configureApiClient, resetApiClient } from './client';
import { getCollection, getJson, postCreate } from './http';
import { ApiNotFound } from './problem';

const stop = z.object({ postcode: z.string() });

beforeEach(() => {
  resetApiClient();
  configureApiClient({ getAccessToken: () => 'test-token' });
});

afterEach(() => {
  resetApiClient();
});

describe('http verbs', () => {
  it('getJson parses the body', async () => {
    worker.use(http.get('/convoys/1', () => HttpResponse.json({ postcode: 'SW1A 1AA' })));

    await expect(getJson('/convoys/1', stop)).resolves.toEqual({ postcode: 'SW1A 1AA' });
  });

  it('getJson throws ApiNotFound for a missing resource', async () => {
    worker.use(http.get('/convoys/999', () => new HttpResponse(null, { status: 404 })));

    await expect(getJson('/convoys/999', stop)).rejects.toBeInstanceOf(ApiNotFound);
  });

  it('getCollection returns items for an existing parent', async () => {
    worker.use(http.get('/convoys/1/route', () => HttpResponse.json([{ postcode: 'M1 1AA' }])));

    await expect(getCollection('/convoys/1/route', stop)).resolves.toEqual([
      { postcode: 'M1 1AA' },
    ]);
  });

  it('getCollection signals a missing parent rather than throwing', async () => {
    worker.use(http.get('/convoys/999/route', () => new HttpResponse(null, { status: 404 })));

    await expect(getCollection('/convoys/999/route', stop)).resolves.toEqual({
      parentMissing: true,
    });
  });

  it('postCreate reads the new id from Location', async () => {
    worker.use(
      http.post(
        '/manifests',
        () =>
          new HttpResponse(null, { status: 201, headers: { Location: '/manifests/UA-2026-01' } }),
      ),
    );

    await expect(postCreate('/manifests', { id: 'UA-2026-01' })).resolves.toEqual({
      id: 'UA-2026-01',
    });
  });
});
