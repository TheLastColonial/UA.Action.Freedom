import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';
import { z } from 'zod';

import { inspectionStatusSchema } from '../../api/schemas/vehicles';
import type {
  CreateVehicleRequest,
  UpdateVehicleRequest,
  VehicleReadModel,
} from '../../api/schemas/vehicles';
import { problem, validationProblem } from './problem';

// Mirrors RecordInspectionRequest + its validator: the status by name, notes up to 2000.
const recordInspectionBodySchema = z.object({
  status: inspectionStatusSchema,
  notes: z.string().max(2000).nullish(),
});

export interface VehicleApi {
  db: Map<string, VehicleReadModel>;
  handlers: RequestHandler[];
}

// The real API never writes the convoy or the inspection through create or update — a new
// vehicle starts on no convoy and Pending, and an edit keeps what was there. `kept` carries that.
function toReadModel(
  vin: string,
  body: CreateVehicleRequest | UpdateVehicleRequest,
  kept: Pick<
    VehicleReadModel,
    'convoyId' | 'inspectionStatus' | 'inspectionNotes' | 'handedOverAt'
  >,
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
    convoyId: kept.convoyId,
    purchaserName: body.purchaserName ?? null,
    purchaseDate: body.purchaseDate ?? null,
    weightKg: body.weightKg,
    maxCargoWeightKg: body.maxCargoWeightKg ?? null,
    cargoWidthCm: body.cargoWidthCm ?? null,
    cargoDepthCm: body.cargoDepthCm ?? null,
    cargoHeightCm: body.cargoHeightCm ?? null,
    inspectionStatus: kept.inspectionStatus,
    inspectionNotes: kept.inspectionNotes,
    handedOverAt: kept.handedOverAt,
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
      db.set(
        body.vin,
        toReadModel(body.vin, body, {
          convoyId: null,
          inspectionStatus: 'Pending',
          inspectionNotes: null,
          handedOverAt: null,
        }),
      );
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/vehicles/${encodeURIComponent(body.vin)}` },
      });
    }),

    http.put('/vehicles/:vin', async ({ params, request }) => {
      const vin = decodeURIComponent(String(params['vin']));
      const existing = db.get(vin);
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as UpdateVehicleRequest;
      db.set(vin, toReadModel(vin, body, existing));
      return new HttpResponse(null, { status: 204 });
    }),

    http.put('/vehicles/:vin/inspection', async ({ params, request }) => {
      const vin = decodeURIComponent(String(params['vin']));
      const existing = db.get(vin);
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      const parsed = recordInspectionBodySchema.safeParse(await request.json());
      if (!parsed.success) {
        return validationProblem({ Status: [parsed.error.message] });
      }
      const notes = parsed.data.notes?.trim() ? parsed.data.notes : null;
      db.set(vin, { ...existing, inspectionStatus: parsed.data.status, inspectionNotes: notes });
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
