import { z } from 'zod';
import type { ZodType } from 'zod';

import type { CreatedResource, ParentMissing } from './client';
import { request } from './client';

type Query = Readonly<Record<string, string | number | boolean | undefined>>;

/** `GET` a single resource. Throws `ApiNotFound` when it does not exist. */
export function getJson<T>(path: string, schema: ZodType<T>, query?: Query): Promise<T> {
  return request({ method: 'GET', path, expect: 'json', schema, ...(query ? { query } : {}) });
}

/**
 * `GET` a sub-resource collection. Resolves to the items, or to `{ parentMissing: true }`
 * when the parent resource itself is absent (404 with an empty body) — distinct from an
 * existing-but-empty collection (`200 []`).
 */
export function getCollection<T>(
  path: string,
  itemSchema: ZodType<T>,
  query?: Query,
): Promise<readonly T[] | ParentMissing> {
  return request({
    method: 'GET',
    path,
    expect: 'collection',
    schema: z.array(itemSchema),
    ...(query ? { query } : {}),
  });
}

/** `POST` a create. The API returns 201 with an empty body; the id comes from `Location`. */
export function postCreate(path: string, body: unknown): Promise<CreatedResource> {
  return request({ method: 'POST', path, expect: 'created', body });
}

/** `PUT` an update. The API returns 204. */
export function put204(path: string, body: unknown): Promise<void> {
  return request({ method: 'PUT', path, expect: 'nocontent', body });
}

/** `DELETE` a resource. The API returns 204. */
export function delete204(path: string): Promise<void> {
  return request({ method: 'DELETE', path, expect: 'nocontent' });
}

/** `POST` a state-machine transition with no body. The API returns 204. */
export function postTransition(path: string): Promise<void> {
  return request({ method: 'POST', path, expect: 'nocontent' });
}
