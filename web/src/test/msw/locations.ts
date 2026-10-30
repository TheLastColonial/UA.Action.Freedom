import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  BayReadModel,
  LoaderAssignment,
  CreateBayRequest,
  CreateLocationRequest,
  LocationReadModel,
} from '../../api/schemas/locations';
import { problem } from './problem';

export interface LocationApi {
  db: Map<number, LocationReadModel>;
  bays: Map<number, BayReadModel>;
  loaders: Map<string, LoaderAssignment>;
  handlers: RequestHandler[];
}

let mintedLocation = 700;
let mintedBay = 900;

export function locationApi(
  seed: readonly LocationReadModel[] = [],
  bySeed: readonly BayReadModel[] = [],
  loaderSeed: readonly LoaderAssignment[] = [],
): LocationApi {
  const db = new Map<number, LocationReadModel>(seed.map((l) => [l.id, l]));
  const bays = new Map<number, BayReadModel>(bySeed.map((b) => [b.id, b]));
  const loaders = new Map<string, LoaderAssignment>(
    loaderSeed.map((a) => [`${String(a.locationId)}/${a.personId}`, a]),
  );
  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));

  const handlers: RequestHandler[] = [
    http.get('/locations', () => HttpResponse.json([...db.values()])),

    http.get('/locations/:id', ({ params }) => {
      const location = db.get(idFrom(params['id']));
      return location ? HttpResponse.json(location) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/locations', async ({ request }) => {
      mintedLocation += 1;
      const body = (await request.json()) as CreateLocationRequest;
      db.set(mintedLocation, {
        id: mintedLocation,
        lastChangedByName: null,
        lastChangedAt: null,
        name: body.name,
        house: body.house ?? null,
        street: body.street ?? null,
        city: body.city ?? null,
        country: body.country ?? null,
        postcode: body.postcode ?? null,
        isRegisteredHub: body.isRegisteredHub ?? false,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/locations/${String(mintedLocation)}` },
      });
    }),

    http.put('/locations/:id', async ({ params, request }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as CreateLocationRequest;
      db.set(id, {
        id,
        lastChangedByName: null,
        lastChangedAt: null,
        name: body.name,
        house: body.house ?? null,
        street: body.street ?? null,
        city: body.city ?? null,
        country: body.country ?? null,
        postcode: body.postcode ?? null,
        isRegisteredHub: body.isRegisteredHub ?? false,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/locations/:id', ({ params }) =>
      db.delete(idFrom(params['id']))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),

    http.get('/locations/:id/bays', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json([...bays.values()].filter((b) => b.locationId === id));
    }),

    http.post('/locations/:id/bays', async ({ params, request }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as CreateBayRequest;
      const existing = [...bays.values()].some((b) => b.locationId === id && b.code === body.code);
      if (existing) {
        return problem(409, `A bay with code '${body.code}' already exists at this location.`);
      }
      mintedBay += 1;
      bays.set(mintedBay, { id: mintedBay, locationId: id, code: body.code });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/locations/${String(id)}/bays/${String(mintedBay)}` },
      });
    }),

    http.get('/locations/:id/loaders', ({ params }) => {
      const id = idFrom(params['id']);
      return db.has(id)
        ? HttpResponse.json([...loaders.values()].filter((a) => a.locationId === id))
        : new HttpResponse(null, { status: 404 });
    }),

    http.put('/locations/:id/loaders/:personId', ({ params }) => {
      const id = idFrom(params['id']);
      const personId = String(params['personId']);
      loaders.set(`${String(id)}/${personId}`, {
        id: loaders.size + 1,
        locationId: id,
        personId,
        personName: 'Assigned Loader',
        from: '2026-10-04T09:00:00',
        until: null,
        lastChangedByName: null,
        lastChangedAt: null,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/locations/:id/loaders/:personId', ({ params }) =>
      loaders.delete(`${String(idFrom(params['id']))}/${String(params['personId'])}`)
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),

    http.delete('/locations/:id/bays/:bayId', ({ params }) =>
      bays.delete(idFrom(params['bayId']))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),
  ];

  return { db, bays, loaders, handlers };
}
