import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  ConvoyReadModel,
  ConvoyVehicleReadModel,
  CreateConvoyRequest,
  ReplaceConvoyRouteRequest,
  RouteStopReadModel,
} from '../../api/schemas/convoys';
import { problem } from './problem';

export interface ConvoyApi {
  db: Map<number, ConvoyReadModel>;
  routes: Map<number, RouteStopReadModel[]>;
  vehicles: Map<number, ConvoyVehicleReadModel[]>;
  handlers: RequestHandler[];
}

let minted = 100;

export function convoyApi(seed: readonly ConvoyReadModel[] = []): ConvoyApi {
  const db = new Map<number, ConvoyReadModel>(seed.map((c) => [c.id, c]));
  const routes = new Map<number, RouteStopReadModel[]>();
  const vehicles = new Map<number, ConvoyVehicleReadModel[]>();

  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));

  const handlers: RequestHandler[] = [
    http.get('/convoys', () => HttpResponse.json([...db.values()])),

    http.get('/convoys/:id', ({ params }) => {
      const convoy = db.get(idFrom(params['id']));
      return convoy ? HttpResponse.json(convoy) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/convoys', async ({ request }) => {
      minted += 1;
      const body = (await request.json()) as CreateConvoyRequest;
      db.set(minted, {
        id: minted,
        start: body.start,
        expectedEnd: body.expectedEnd,
        truckListPublishedAt: null,
        truckListPublished: false,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/convoys/${String(minted)}` },
      });
    }),

    http.put('/convoys/:id', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const existing = db.get(id);
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as CreateConvoyRequest;
      db.set(id, { ...existing, start: body.start, expectedEnd: body.expectedEnd });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/convoys/:id', ({ params }) =>
      db.delete(idFrom(params['id']))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),

    http.get('/convoys/:id/route', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(routes.get(id) ?? []);
    }),

    http.put('/convoys/:id/route', async ({ params, request }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as ReplaceConvoyRouteRequest;
      routes.set(
        id,
        body.stops.map((stop, index) => ({
          sequence: index + 1,
          house: stop.house ?? null,
          street: stop.street ?? null,
          city: stop.city ?? null,
          country: stop.country ?? null,
          postcode: stop.postcode,
        })),
      );
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/convoys/:id/vehicles', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(vehicles.get(id) ?? []);
    }),

    http.put('/convoys/:id/vehicles/:vin', ({ params }) => {
      const id = idFrom(params['id']);
      const convoy = db.get(id);
      if (!convoy) {
        return new HttpResponse(null, { status: 404 });
      }
      if (convoy.truckListPublished) {
        return problem(409, 'The truck list for this convoy has been published.');
      }
      const vin = decodeURIComponent(String(params['vin']));
      const list = vehicles.get(id) ?? [];
      if (!list.some((v) => v.vin === vin)) {
        list.push({ vin, plate: `PL-${vin.slice(-4)}`, weightKg: 1800 });
      }
      vehicles.set(id, list);
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/convoys/:id/vehicles/:vin', ({ params }) => {
      const id = idFrom(params['id']);
      const convoy = db.get(id);
      if (!convoy) {
        return new HttpResponse(null, { status: 404 });
      }
      if (convoy.truckListPublished) {
        return problem(409, 'The truck list for this convoy has been published.');
      }
      const vin = decodeURIComponent(String(params['vin']));
      const list = vehicles.get(id) ?? [];
      const next = list.filter((v) => v.vin !== vin);
      if (next.length === list.length) {
        return new HttpResponse(null, { status: 404 });
      }
      vehicles.set(id, next);
      return new HttpResponse(null, { status: 204 });
    }),

    http.post('/convoys/:id/publish-truck-list', ({ params }) => {
      const id = idFrom(params['id']);
      const convoy = db.get(id);
      if (!convoy) {
        return new HttpResponse(null, { status: 404 });
      }
      if (convoy.truckListPublished) {
        return problem(409, 'The truck list for this convoy has already been published.');
      }
      db.set(id, {
        ...convoy,
        truckListPublished: true,
        truckListPublishedAt: '2026-03-01T00:00:00',
      });
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { db, routes, vehicles, handlers };
}
