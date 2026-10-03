import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import { customsAuthoritySchema } from '../../api/schemas/categories';
import type {
  CreateCategoryRequest,
  ItemCategoryReadModel,
  SetCategoryCodeRequest,
} from '../../api/schemas/categories';
import { problem } from './problem';

export interface CategoryApi {
  db: Map<number, ItemCategoryReadModel>;
  handlers: RequestHandler[];
}

let minted = 900;

// A small in-memory store fronted by handlers that match the real routes and status codes. A name is
// unique, a created category is never fixed, and an update keeps what it cannot change (fixed, codes).
export function categoryApi(seed: readonly ItemCategoryReadModel[] = []): CategoryApi {
  const db = new Map<number, ItemCategoryReadModel>(seed.map((c) => [c.id, c]));
  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const nameTaken = (name: string, exceptId: number) =>
    [...db.values()].some(
      (c) => c.id !== exceptId && c.nameEn.toLowerCase() === name.toLowerCase(),
    );

  const handlers: RequestHandler[] = [
    http.get('/categories', () =>
      HttpResponse.json(
        [...db.values()].sort(
          (a, b) => Number(b.isFixed) - Number(a.isFixed) || a.nameEn.localeCompare(b.nameEn),
        ),
      ),
    ),

    http.get('/categories/:id', ({ params }) => {
      const category = db.get(idFrom(params['id']));
      return category ? HttpResponse.json(category) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/categories', async ({ request }) => {
      const body = (await request.json()) as CreateCategoryRequest;
      if (nameTaken(body.nameEn, 0)) {
        return problem(409, `A category named '${body.nameEn}' already exists.`);
      }
      minted += 1;
      db.set(minted, {
        id: minted,
        nameEn: body.nameEn,
        nameUk: body.nameUk ?? '',
        isFixed: false,
        hazardClass: body.hazardClass ?? null,
        isSensitive: body.isSensitive,
        isNotCarried: body.isNotCarried,
        warnWithinDays: body.warnWithinDays ?? null,
        ukCode: null,
        euCode: null,
        uaCode: null,
        lastChangedByName: null,
        lastChangedAt: null,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/categories/${String(minted)}` },
      });
    }),

    http.put('/categories/:id', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const existing = db.get(id);
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as CreateCategoryRequest;
      if (nameTaken(body.nameEn, id)) {
        return problem(409, `A category named '${body.nameEn}' already exists.`);
      }
      db.set(id, {
        ...existing,
        nameEn: body.nameEn,
        nameUk: body.nameUk ?? '',
        hazardClass: body.hazardClass ?? null,
        isSensitive: body.isSensitive,
        isNotCarried: body.isNotCarried,
        warnWithinDays: body.warnWithinDays ?? null,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.put('/categories/:id/codes/:authority', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const existing = db.get(id);
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      const authority = customsAuthoritySchema.safeParse(String(params['authority']).toUpperCase());
      if (!authority.success) {
        return problem(400, 'The authority must be UK, EU or UA.');
      }
      const { code } = (await request.json()) as SetCategoryCodeRequest;
      const key = { UK: 'ukCode', EU: 'euCode', UA: 'uaCode' } as const;
      db.set(id, { ...existing, [key[authority.data]]: code });
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { db, handlers };
}
