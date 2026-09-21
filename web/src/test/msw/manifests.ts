import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { ManifestStatus } from '../../api/schemas/common';
import type { VehicleCrewReadModel } from '../../api/schemas/convoys';
import type {
  CreateConvoyVehicleManifestRequest,
  ManifestBoxReadModel,
  ManifestReadModel,
  UpdateManifestRequest,
} from '../../api/schemas/manifests';
import { problem } from './problem';

interface EdgeRule {
  verb: string;
  from: readonly ManifestStatus[];
  to: ManifestStatus;
}

const EDGES: readonly EdgeRule[] = [
  { verb: 'propose', from: ['Created', 'Rejected'], to: 'Proposed' },
  { verb: 'reject', from: ['Created', 'Proposed'], to: 'Rejected' },
  { verb: 'approve', from: ['Proposed'], to: 'Confirmed' },
  { verb: 'prepare', from: ['Confirmed'], to: 'Preparing' },
  { verb: 'ready', from: ['Preparing'], to: 'Ready' },
  { verb: 'depart', from: ['Ready'], to: 'InTransit' },
  { verb: 'deliver', from: ['InTransit'], to: 'Delivered' },
  { verb: 'lose', from: ['InTransit'], to: 'Lost' },
  { verb: 'return', from: ['Delivered'], to: 'Returned' },
];

export interface VehicleCargoCapacity {
  maxCargoWeightKg?: number | null;
  cargoWidthCm?: number | null;
  cargoDepthCm?: number | null;
  cargoHeightCm?: number | null;
}

export interface ManifestApiOptions {
  publishedConvoyIds?: readonly number[];
  /** VINs whose insurance is recorded, not voided and in cover — the API refuses `depart` otherwise. */
  insuredVins?: readonly string[];
  /** The crew each manifest reports, keyed by manifest id. */
  crewByManifest?: ReadonlyMap<string, readonly VehicleCrewReadModel[]>;
  vehicleCargoCapacity?: VehicleCargoCapacity;
}

export interface ManifestApi {
  db: Map<string, ManifestReadModel>;
  /** The crew a manifest reports, keyed by manifest id. Written on the convoy, read here. */
  crew: Map<string, VehicleCrewReadModel[]>;
  boxes: Map<string, ManifestBoxReadModel[]>;
  handlers: RequestHandler[];
}

// Mirrors GetManifestWeightHandler.FitsCargoSpace: fits (i.e. not flagged) whenever either side
// has no recorded dimensions, and otherwise compares both sets sorted largest-to-smallest so a
// box can be flagged as fitting in any rotation.
function fitsCargoSpace(box: ManifestBoxReadModel, capacity: VehicleCargoCapacity): boolean {
  const boxDims = [box.widthCm, box.depthCm, box.heightCm];
  if (boxDims.some((d) => d === null)) {
    return true;
  }
  const cargoDims = [
    capacity.cargoWidthCm ?? null,
    capacity.cargoDepthCm ?? null,
    capacity.cargoHeightCm ?? null,
  ];
  if (cargoDims.some((d) => d === null)) {
    return true;
  }
  const bSorted = [...(boxDims as number[])].sort((a, b) => b - a);
  const cSorted = [...(cargoDims as number[])].sort((a, b) => b - a);
  return bSorted.every((dim, i) => dim <= (cSorted[i] ?? Number.POSITIVE_INFINITY));
}

const FREEZE_MESSAGE =
  'A Goods Movement Reference has been created for this manifest, so it can no longer be changed.';

export function manifestApi(
  seed: readonly ManifestReadModel[] = [],
  options: ManifestApiOptions = {},
): ManifestApi {
  const db = new Map<string, ManifestReadModel>(seed.map((m) => [m.id, m]));
  const seededCrew: ReadonlyMap<string, readonly VehicleCrewReadModel[]> =
    options.crewByManifest ?? new Map<string, readonly VehicleCrewReadModel[]>();
  const crew = new Map<string, VehicleCrewReadModel[]>(
    [...seededCrew].map(([id, members]) => [id, [...members]]),
  );
  const boxes = new Map<string, ManifestBoxReadModel[]>();
  const publishedConvoys = new Set(options.publishedConvoyIds ?? []);
  const insuredVins = new Set(options.insuredVins ?? []);
  const idFrom = (raw: string | readonly string[] | undefined) => decodeURIComponent(String(raw));

  const handlers: RequestHandler[] = [
    http.get('/manifests', () => HttpResponse.json([...db.values()])),

    http.get('/manifests/:id', ({ params }) => {
      const manifest = db.get(idFrom(params['id']));
      return manifest ? HttpResponse.json(manifest) : new HttpResponse(null, { status: 404 });
    }),

    // There is no POST /manifests. A manifest is the paperwork for one vehicle on one convoy, so
    // it is opened on that truck-list entry — which is why this handler lives on a convoy route.
    http.post('/convoys/:convoyId/vehicles/:vin/manifest', async ({ params, request }) => {
      const convoyId = Number(String(params['convoyId']));
      const vin = decodeURIComponent(String(params['vin']));
      const body = (await request.json()) as CreateConvoyVehicleManifestRequest;

      if (db.has(body.id)) {
        return problem(409, `A manifest with reference '${body.id}' already exists.`);
      }
      // One manifest per vehicle per convoy: arrival asks each vehicle for its finished manifest
      // and has to get one answer.
      if ([...db.values()].some((m) => m.convoyId === convoyId && m.vin === vin)) {
        return problem(409, `Vehicle '${vin}' already has a manifest on this convoy.`);
      }

      db.set(body.id, {
        id: body.id,
        convoyId,
        vin,
        status: 'Created',
        deliveryNotes: body.deliveryNotes ?? null,
        ferryBookingComplete: body.ferryBookingComplete,
        gmrSubmittedAt: null,
        frozen: false,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/manifests/${encodeURIComponent(body.id)}` },
      });
    }),

    http.put('/manifests/:id', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const manifest = db.get(id);
      if (!manifest) {
        return new HttpResponse(null, { status: 404 });
      }
      if (manifest.frozen) {
        return problem(409, FREEZE_MESSAGE);
      }
      const body = (await request.json()) as UpdateManifestRequest;
      // The convoy and the vehicle are the manifest's identity: the UPDATE never names those
      // columns, so anything sent for them is read straight past.
      db.set(id, {
        ...manifest,
        deliveryNotes: body.deliveryNotes ?? null,
        ferryBookingComplete: body.ferryBookingComplete,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/manifests/:id', ({ params }) => {
      const id = idFrom(params['id']);
      const manifest = db.get(id);
      if (!manifest) {
        return new HttpResponse(null, { status: 404 });
      }
      if (manifest.frozen) {
        return problem(409, FREEZE_MESSAGE);
      }
      db.delete(id);
      return new HttpResponse(null, { status: 204 });
    }),

    // A read. There is no PUT: crewing happens on the convoy's truck-list entry, and this
    // reports what is recorded there.
    http.get('/manifests/:id/crew', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(crew.get(id) ?? []);
    }),

    http.get('/manifests/:id/boxes', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(boxes.get(id) ?? []);
    }),

    http.put('/manifests/:id/boxes/:boxId', ({ params }) => {
      const id = idFrom(params['id']);
      const manifest = db.get(id);
      if (!manifest) {
        return new HttpResponse(null, { status: 404 });
      }
      if (manifest.frozen) {
        return problem(409, FREEZE_MESSAGE);
      }
      const boxId = Number(params['boxId']);
      const list = boxes.get(id) ?? [];
      if (!list.some((b) => b.boxId === boxId)) {
        list.push({
          boxId,
          weightKg: 15,
          validated: true,
          widthCm: null,
          depthCm: null,
          heightCm: null,
        });
      }
      boxes.set(id, list);
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/manifests/:id/boxes/:boxId', ({ params }) => {
      const id = idFrom(params['id']);
      const manifest = db.get(id);
      if (!manifest) {
        return new HttpResponse(null, { status: 404 });
      }
      if (manifest.frozen) {
        return problem(409, FREEZE_MESSAGE);
      }
      const boxId = Number(params['boxId']);
      const list = boxes.get(id) ?? [];
      const next = list.filter((b) => b.boxId !== boxId);
      if (next.length === list.length) {
        return new HttpResponse(null, { status: 404 });
      }
      boxes.set(id, next);
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/manifests/:id/weight', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const list = boxes.get(id) ?? [];
      const cargoKg = list.reduce((total, b) => total + b.weightKg, 0);
      const unvalidatedBoxCount = list.filter((b) => !b.validated).length;

      const capacity = options.vehicleCargoCapacity ?? {};
      const maxCargoWeightKg = capacity.maxCargoWeightKg ?? null;
      const cargoOverweight = maxCargoWeightKg !== null && cargoKg > maxCargoWeightKg;
      const oversizedBoxIds = list
        .filter((box) => !fitsCargoSpace(box, capacity))
        .map((box) => box.boxId);

      return HttpResponse.json({
        vehicleKg: 2000,
        cargoKg,
        crewAndBagsKg: 200,
        fuelKg: 45,
        totalKg: 2000 + cargoKg + 200 + 45,
        unvalidatedBoxCount,
        maxCargoWeightKg,
        cargoOverweight,
        oversizedBoxIds,
      });
    }),

    ...EDGES.map((edge) =>
      http.post(`/manifests/:id/${edge.verb}`, ({ params }) => {
        const id = idFrom(params['id']);
        const manifest = db.get(id);
        if (!manifest) {
          return new HttpResponse(null, { status: 404 });
        }
        if (manifest.frozen && (edge.verb === 'propose' || edge.verb === 'reject')) {
          return problem(409, FREEZE_MESSAGE);
        }
        if (!edge.from.includes(manifest.status)) {
          return problem(409, 'A manifest cannot move to that state from the one it is in.');
        }
        if (edge.verb === 'propose') {
          const published = publishedConvoys.has(manifest.convoyId);
          if (!published) {
            return problem(
              409,
              "This manifest's convoy has not published its truck list, so it cannot be proposed.",
            );
          }
        }
        if (edge.verb === 'depart' && !insuredVins.has(manifest.vin)) {
          return problem(
            409,
            'This vehicle cannot depart: its insurance is not recorded, was voided by a crew change, or does not cover today. Record the insurance for its current crew first.',
          );
        }
        db.set(id, {
          ...manifest,
          status: edge.to,
          ...(edge.verb === 'approve'
            ? { frozen: true, gmrSubmittedAt: '2026-05-01T00:00:00' }
            : {}),
        });
        return new HttpResponse(null, { status: 204 });
      }),
    ),
  ];

  return { db, crew, boxes, handlers };
}
