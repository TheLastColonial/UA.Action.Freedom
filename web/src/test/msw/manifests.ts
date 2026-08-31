import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { ManifestStatus } from '../../api/schemas/common';
import type {
  CreateManifestRequest,
  ManifestBoxReadModel,
  ManifestDriverTeamReadModel,
  ManifestReadModel,
  SetManifestTeamRequest,
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

export interface ManifestApiOptions {
  publishedConvoyIds?: readonly number[];
  knownDriverIds?: readonly string[];
}

export interface ManifestApi {
  db: Map<string, ManifestReadModel>;
  teams: Map<string, ManifestDriverTeamReadModel[]>;
  boxes: Map<string, ManifestBoxReadModel[]>;
  handlers: RequestHandler[];
}

const FREEZE_MESSAGE =
  'A Goods Movement Reference has been created for this manifest, so it can no longer be changed.';

export function manifestApi(
  seed: readonly ManifestReadModel[] = [],
  options: ManifestApiOptions = {},
): ManifestApi {
  const db = new Map<string, ManifestReadModel>(seed.map((m) => [m.id, m]));
  const teams = new Map<string, ManifestDriverTeamReadModel[]>();
  const boxes = new Map<string, ManifestBoxReadModel[]>();
  const publishedConvoys = new Set(options.publishedConvoyIds ?? []);
  const drivers = new Set(options.knownDriverIds ?? []);
  const idFrom = (raw: string | readonly string[] | undefined) => decodeURIComponent(String(raw));

  const handlers: RequestHandler[] = [
    http.get('/manifests', () => HttpResponse.json([...db.values()])),

    http.get('/manifests/:id', ({ params }) => {
      const manifest = db.get(idFrom(params['id']));
      return manifest ? HttpResponse.json(manifest) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/manifests', async ({ request }) => {
      const body = (await request.json()) as CreateManifestRequest;
      if (db.has(body.id)) {
        return problem(409, `A manifest with reference '${body.id}' already exists.`);
      }
      db.set(body.id, {
        id: body.id,
        vin: body.vin ?? null,
        convoyId: body.convoyId ?? null,
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
      db.set(id, {
        ...manifest,
        vin: body.vin ?? null,
        convoyId: body.convoyId ?? null,
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

    http.get('/manifests/:id/teams', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(teams.get(id) ?? []);
    }),

    http.put('/manifests/:id/teams/:leg', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const manifest = db.get(id);
      if (!manifest) {
        return new HttpResponse(null, { status: 404 });
      }
      if (manifest.frozen) {
        return problem(409, FREEZE_MESSAGE);
      }
      const leg = String(params['leg']) === 'Border' ? 'Border' : 'Uk';
      const body = (await request.json()) as SetManifestTeamRequest;
      if (drivers.size > 0 && !drivers.has(body.primaryPersonId)) {
        return problem(404, 'One of the volunteers named for this leg is not on file.');
      }
      if (body.secondaryPersonId && body.secondaryPersonId === body.primaryPersonId) {
        return problem(
          409,
          'A driver team is two people; the same volunteer cannot crew both halves of it.',
        );
      }
      const list = (teams.get(id) ?? []).filter((t) => t.leg !== leg);
      list.push({
        leg,
        primaryPersonId: body.primaryPersonId,
        secondaryPersonId: body.secondaryPersonId ?? null,
      });
      teams.set(id, list);
      return new HttpResponse(null, { status: 204 });
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
        list.push({ boxId, weightKg: 15, validated: true });
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
      return HttpResponse.json({
        vehicleKg: 2000,
        cargoKg,
        crewAndBagsKg: 200,
        fuelKg: 45,
        totalKg: 2000 + cargoKg + 200 + 45,
        unvalidatedBoxCount,
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
          const published = manifest.convoyId !== null && publishedConvoys.has(manifest.convoyId);
          if (!published) {
            return problem(
              409,
              "This manifest's convoy has not published its truck list, so it cannot be proposed.",
            );
          }
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

  return { db, teams, boxes, handlers };
}
