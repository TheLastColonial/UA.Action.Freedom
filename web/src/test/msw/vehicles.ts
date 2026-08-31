import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  CreateVehicleRequest,
  UpdateVehicleRequest,
  VehicleReadModel,
} from '../../api/schemas/vehicles';
import { problem } from './problem';

export interface VehicleApi {
  db: Map<string, VehicleReadModel>;
  handlers: RequestHandler[];
}

function toReadModel(
  vin: string,
  body: CreateVehicleRequest | UpdateVehicleRequest,
): VehicleReadModel {
  return {
    vin,
    plate: body.plate,
    brand: body.brand ?? null,
    model: body.model ?? null,
    colour: body.colour ?? null,
    transmission: body.transmission,
    notes: body.notes ?? null,
    mileage: body.mileage ?? null,
    servicing: body.servicing,
    year: body.year,
    fuel: body.fuel,
    convoyId: body.convoyId ?? null,
    purchaserName: body.purchaserName ?? null,
    purchaseDate: body.purchaseDate ?? null,
    weightKg: body.weightKg,
  };
}

// A small in-memory store fronted by handlers that match the real routes and status codes,
// so create-then-read works within a single test. Register with `worker.use(...api.handlers)`.
export function vehicleApi(seed: readonly VehicleReadModel[] = []): VehicleApi {
  const db = new Map<string, VehicleReadModel>(seed.map((v) => [v.vin, v]));

  const handlers: RequestHandler[] = [
    http.get('/vehicles', ({ request }) => {
      const url = new URL(request.url);
      const page = Math.max(1, Number(url.searchParams.get('page') ?? '1'));
      const pageSize = Number(url.searchParams.get('pageSize') ?? '50');
      const all = [...db.values()];
      const start = (page - 1) * pageSize;
      return HttpResponse.json(all.slice(start, start + pageSize));
    }),

    http.get('/vehicles/:vin', ({ params }) => {
      const vehicle = db.get(decodeURIComponent(String(params['vin'])));
      return vehicle ? HttpResponse.json(vehicle) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/vehicles', async ({ request }) => {
      const body = (await request.json()) as CreateVehicleRequest;
      if (db.has(body.vin)) {
        return problem(409, `A vehicle with VIN '${body.vin}' already exists.`);
      }
      db.set(body.vin, toReadModel(body.vin, body));
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/vehicles/${encodeURIComponent(body.vin)}` },
      });
    }),

    http.put('/vehicles/:vin', async ({ params, request }) => {
      const vin = decodeURIComponent(String(params['vin']));
      if (!db.has(vin)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as UpdateVehicleRequest;
      db.set(vin, toReadModel(vin, body));
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/vehicles/:vin', ({ params }) => {
      const vin = decodeURIComponent(String(params['vin']));
      return db.delete(vin)
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 });
    }),
  ];

  return { db, handlers };
}
