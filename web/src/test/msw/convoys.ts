import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  ConvoyReadModel,
  ConvoyVehicleReadModel,
  CreateConvoyRequest,
  ReplaceConvoyRouteRequest,
  RouteStopReadModel,
  VehicleDriverReadModel,
} from '../../api/schemas/convoys';
import type { PersonReadModel } from '../../api/schemas/people';
import type { VehicleReadModel } from '../../api/schemas/vehicles';
import { problem } from './problem';

/**
 * What the truck-list and crew routes look up, as the real API does. Pass the `db` of a
 * `vehicleApi` / `personApi` so a test that assigns a vehicle or a driver sees the same
 * rules the API enforces: an unknown VIN is a 404, a vehicle that has not passed its
 * inspection or is on another convoy is a 409, and only a registered driver can crew.
 */
export interface ConvoyApiLookups {
  fleet?: ReadonlyMap<string, VehicleReadModel>;
  people?: ReadonlyMap<string, PersonReadModel>;
}

export interface ConvoyApi {
  db: Map<number, ConvoyReadModel>;
  routes: Map<number, RouteStopReadModel[]>;
  vehicles: Map<number, ConvoyVehicleReadModel[]>;
  drivers: Map<string, VehicleDriverReadModel[]>;
  handlers: RequestHandler[];
}

let minted = 100;

export function convoyApi(
  seed: readonly ConvoyReadModel[] = [],
  { fleet = new Map(), people = new Map() }: ConvoyApiLookups = {},
): ConvoyApi {
  const db = new Map<number, ConvoyReadModel>(seed.map((c) => [c.id, c]));
  const routes = new Map<number, RouteStopReadModel[]>();
  const vehicles = new Map<number, ConvoyVehicleReadModel[]>();
  const drivers = new Map<string, VehicleDriverReadModel[]>();

  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const driverKey = (convoyId: number, vin: string) => `${String(convoyId)}:${vin}`;
  const onConvoy = (convoyId: number, vin: string) =>
    (vehicles.get(convoyId) ?? []).some((v) => v.vin === vin);
  const convoyOf = (vin: string) =>
    [...vehicles.entries()].find(([, list]) => list.some((v) => v.vin === vin))?.[0];
  const setDriverCount = (convoyId: number, vin: string) => {
    const count = (drivers.get(driverKey(convoyId, vin)) ?? []).length;
    vehicles.set(
      convoyId,
      (vehicles.get(convoyId) ?? []).map((v) => (v.vin === vin ? { ...v, driverCount: count } : v)),
    );
  };

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
      const vehicle = fleet.get(vin);
      if (!vehicle) {
        return problem(404, `There is no vehicle with VIN '${vin}'.`);
      }
      if (vehicle.inspectionStatus !== 'Passed') {
        return problem(
          409,
          `Vehicle '${vin}' has not passed its servicing inspection, so it cannot join a convoy.`,
        );
      }
      const current = convoyOf(vin);
      if (current !== undefined && current !== id) {
        return problem(
          409,
          `Vehicle '${vin}' is already on another convoy. Remove it from that convoy first.`,
        );
      }
      if (current === undefined) {
        vehicles.set(id, [
          ...(vehicles.get(id) ?? []),
          { vin, plate: vehicle.plate, weightKg: vehicle.weightKg, driverCount: 0 },
        ]);
      }
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
      drivers.delete(driverKey(id, vin));
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

    http.get('/convoys/:id/vehicles/:vin/drivers', ({ params }) => {
      const convoyId = idFrom(params['id']);
      const vin = decodeURIComponent(String(params['vin']));
      if (!db.has(convoyId) || !onConvoy(convoyId, vin)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(drivers.get(driverKey(convoyId, vin)) ?? []);
    }),

    http.put('/convoys/:id/vehicles/:vin/drivers/:personId', ({ params }) => {
      const convoyId = idFrom(params['id']);
      if (!db.has(convoyId)) {
        return new HttpResponse(null, { status: 404 });
      }
      const vin = decodeURIComponent(String(params['vin']));
      const person = people.get(String(params['personId']));
      if (!person) {
        return problem(404, 'There is no volunteer with that ID.');
      }
      if (!person.isDriver) {
        return problem(422, 'That volunteer is not registered as a driver.');
      }
      if (!onConvoy(convoyId, vin)) {
        return problem(404, `There is no vehicle with VIN '${vin}' on this convoy.`);
      }
      const key = driverKey(convoyId, vin);
      const crew = drivers.get(key) ?? [];
      if (crew.some((d) => d.personId === person.id)) {
        return problem(409, 'That driver is already assigned to this vehicle.');
      }
      drivers.set(key, [
        ...crew,
        { personId: person.id, firstName: person.firstName, lastName: person.lastName },
      ]);
      setDriverCount(convoyId, vin);
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/convoys/:id/vehicles/:vin/drivers/:personId', ({ params }) => {
      const convoyId = idFrom(params['id']);
      const vin = decodeURIComponent(String(params['vin']));
      if (!db.has(convoyId) || !onConvoy(convoyId, vin)) {
        return new HttpResponse(null, { status: 404 });
      }
      const key = driverKey(convoyId, vin);
      const crew = drivers.get(key) ?? [];
      const next = crew.filter((d) => d.personId !== String(params['personId']));
      if (next.length === crew.length) {
        return new HttpResponse(null, { status: 404 });
      }
      drivers.set(key, next);
      setDriverCount(convoyId, vin);
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { db, routes, vehicles, drivers, handlers };
}
