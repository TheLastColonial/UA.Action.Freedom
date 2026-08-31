import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  AddBoxItemRequest,
  BoxItemReadModel,
  BoxReadModel,
  CreateBoxRequest,
  ValidateBoxRequest,
} from '../../api/schemas/boxes';
import { problem } from './problem';

export interface BoxApi {
  db: Map<number, BoxReadModel>;
  items: Map<number, BoxItemReadModel[]>;
  handlers: RequestHandler[];
}

let mintedBox = 500;
let mintedItem = 0;

export function boxApi(
  seed: readonly BoxReadModel[] = [],
  knownVolunteerIds: readonly string[] = [],
): BoxApi {
  const db = new Map<number, BoxReadModel>(seed.map((b) => [b.id, b]));
  const items = new Map<number, BoxItemReadModel[]>();
  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const validators = new Set(knownVolunteerIds);

  const validatedGuard = (box: BoxReadModel | undefined) =>
    box?.validated
      ? problem(409, 'This box has been validated and can no longer be changed.')
      : null;

  const handlers: RequestHandler[] = [
    http.get('/boxes', () => HttpResponse.json([...db.values()])),

    http.get('/boxes/:id', ({ params }) => {
      const box = db.get(idFrom(params['id']));
      return box ? HttpResponse.json(box) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/boxes', async ({ request }) => {
      mintedBox += 1;
      const body = (await request.json()) as CreateBoxRequest;
      db.set(mintedBox, {
        id: mintedBox,
        weightKg: 0,
        receiverRef: body.receiverRef ?? null,
        house: body.house ?? null,
        street: body.street ?? null,
        city: body.city ?? null,
        country: body.country ?? null,
        postcode: body.postcode ?? null,
        validatedByPersonId: null,
        validatedAt: null,
        validated: false,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/boxes/${String(mintedBox)}` },
      });
    }),

    http.put('/boxes/:id', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const frozen = validatedGuard(box);
      if (frozen) {
        return frozen;
      }
      const body = (await request.json()) as CreateBoxRequest;
      db.set(id, {
        ...box,
        receiverRef: body.receiverRef ?? null,
        house: body.house ?? null,
        street: body.street ?? null,
        city: body.city ?? null,
        country: body.country ?? null,
        postcode: body.postcode ?? null,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/boxes/:id', ({ params }) =>
      db.delete(idFrom(params['id']))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),

    http.get('/boxes/:id/items', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(items.get(id) ?? []);
    }),

    http.post('/boxes/:id/items', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const frozen = validatedGuard(box);
      if (frozen) {
        return frozen;
      }
      mintedItem += 1;
      const body = (await request.json()) as AddBoxItemRequest;
      const list = items.get(id) ?? [];
      list.push({
        id: `bbbbbbbb-0000-0000-0000-${String(mintedItem).padStart(12, '0')}`,
        description: body.description,
        properties: body.properties,
      });
      items.set(id, list);
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/boxes/:id/items/:itemId', ({ params }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const frozen = validatedGuard(box);
      if (frozen) {
        return frozen;
      }
      const itemId = String(params['itemId']);
      const list = items.get(id) ?? [];
      const next = list.filter((i) => i.id !== itemId);
      if (next.length === list.length) {
        return new HttpResponse(null, { status: 404 });
      }
      items.set(id, next);
      return new HttpResponse(null, { status: 204 });
    }),

    http.post('/boxes/:id/validate', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      if (box.validated) {
        return problem(409, 'This box has already been validated.');
      }
      const body = (await request.json()) as ValidateBoxRequest;
      if (validators.size > 0 && !validators.has(body.validatedByPersonId)) {
        return problem(404, 'The volunteer named as having checked this box is not on file.');
      }
      db.set(id, {
        ...box,
        validated: true,
        validatedByPersonId: body.validatedByPersonId,
        validatedAt: '2026-04-01T00:00:00',
        weightKg: body.weightKg,
      });
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { db, items, handlers };
}
