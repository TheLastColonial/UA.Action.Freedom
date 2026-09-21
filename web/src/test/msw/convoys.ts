import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';
import { z } from 'zod';

import type {
  ConvoyReadModel,
  ConvoyVehicleReadModel,
  CreateConvoyRequest,
  ReplaceConvoyRouteRequest,
  RouteStopReadModel,
  VehicleCrewReadModel,
  VehicleInsuranceReadModel,
} from '../../api/schemas/convoys';
import type { ManifestStatus } from '../../api/schemas/common';
import type { PersonReadModel } from '../../api/schemas/people';
import type { VehicleReadModel } from '../../api/schemas/vehicles';
import { crewRoleSchema } from '../../api/schemas/convoys';
import { journeyLegSchema } from '../../api/schemas/common';
import { problem, validationProblem } from './problem';

const FINISHED: readonly ManifestStatus[] = ['Delivered', 'Lost', 'Returned'];

// Mirrors RecordInsuranceRequest and its validator.
const insuranceBodySchema = z.object({
  insurer: z.string().min(1).max(200),
  policyNumber: z.string().min(1).max(100),
  coverStart: z.string(),
  coverEnd: z.string(),
  costGbp: z.number().min(0).optional(),
});

// Mirrors AssignCrewRequest: the leg is required — a vehicle is crewed twice, with a handover at
// the European border — and the role defaults to Driver.
const crewBodySchema = z.object({ leg: journeyLegSchema, role: crewRoleSchema.optional() });

/**
 * What the truck-list and crew routes look up, as the real API does. Pass the `db` of a
 * `vehicleApi` / `personApi` so a test that assigns a vehicle or a driver sees the same
 * rules the API enforces: an unknown VIN is a 404, a vehicle that has not passed its
 * inspection or is on another convoy is a 409, and only a registered driver can crew.
 */
export interface ConvoyApiLookups {
  /** Written to on arrival — handed-over vehicles get `handedOverAt`, returned ones leave. */
  fleet?: Map<string, VehicleReadModel>;
  people?: ReadonlyMap<string, PersonReadModel>;
  /** The status of each vehicle's manifest on its convoy, which is what arrival checks. */
  manifestStatusByVin?: ReadonlyMap<string, ManifestStatus>;
}

export interface ConvoyApi {
  db: Map<number, ConvoyReadModel>;
  routes: Map<number, RouteStopReadModel[]>;
  vehicles: Map<number, ConvoyVehicleReadModel[]>;
  crew: Map<string, VehicleCrewReadModel[]>;
  /** Keyed `${convoyId}:${vin}`, like `crew`. */
  insurance: Map<string, VehicleInsuranceReadModel>;
  handlers: RequestHandler[];
}

let minted = 100;

export function convoyApi(
  seed: readonly ConvoyReadModel[] = [],
  { fleet = new Map(), people = new Map(), manifestStatusByVin = new Map() }: ConvoyApiLookups = {},
): ConvoyApi {
  const db = new Map<number, ConvoyReadModel>(seed.map((c) => [c.id, c]));
  const routes = new Map<number, RouteStopReadModel[]>();
  const vehicles = new Map<number, ConvoyVehicleReadModel[]>();
  const crew = new Map<string, VehicleCrewReadModel[]>();
  const insurance = new Map<string, VehicleInsuranceReadModel>();

  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const crewKey = (convoyId: number, vin: string) => `${String(convoyId)}:${vin}`;
  const entryFor = (convoyId: number, vin: string) =>
    (vehicles.get(convoyId) ?? []).find((v) => v.vin === vin);
  const onConvoy = (convoyId: number, vin: string) => entryFor(convoyId, vin) !== undefined;
  // "On another convoy" means travelling with one that has not arrived: a withdrawn vehicle, or
  // one whose convoy has arrived, is free for the next.
  const convoyOf = (vin: string) =>
    [...vehicles.entries()].find(
      ([convoyId, list]) =>
        list.some((v) => v.vin === vin && !v.withdrawn) && db.get(convoyId)?.arrived !== true,
    )?.[0];
  // The policy names the crew, so any crew change voids it — as ConvoyRepository does.
  const voidInsurance = (convoyId: number, vin: string) => {
    const key = crewKey(convoyId, vin);
    const policy = insurance.get(key);
    if (policy && !policy.voided) {
      insurance.set(key, { ...policy, voided: true, voidedAt: '2026-09-01T10:00:00' });
    }
  };

  // Crew counts are per leg, as the SQL's conditional aggregates compute them.
  const setCrewCounts = (convoyId: number, vin: string) => {
    const members = crew.get(crewKey(convoyId, vin)) ?? [];
    const count = (leg: string, role: string) =>
      members.filter((member) => member.leg === leg && member.role === role).length;

    vehicles.set(
      convoyId,
      (vehicles.get(convoyId) ?? []).map((v) =>
        v.vin === vin
          ? {
              ...v,
              ukDriverCount: count('Uk', 'Driver'),
              ukPassengerCount: count('Uk', 'Passenger'),
              borderDriverCount: count('Border', 'Driver'),
              borderPassengerCount: count('Border', 'Passenger'),
            }
          : v,
      ),
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
        arrivedAt: null,
        arrived: false,
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
      if (vehicle.handedOverAt !== null) {
        return problem(
          409,
          `Vehicle '${vin}' was handed over in Ukraine at the end of an earlier convoy.`,
        );
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
          {
            vin,
            plate: vehicle.plate,
            weightKg: vehicle.weightKg,
            ukDriverCount: 0,
            ukPassengerCount: 0,
            borderDriverCount: 0,
            borderPassengerCount: 0,
            withdrawnAt: null,
            withdrawnReason: null,
            travelling: true,
            withdrawn: false,
          },
        ]);
      }
      return new HttpResponse(null, { status: 204 });
    }),

    // Before publication the entry goes, with its crew and insurance. Afterwards the vehicle is
    // withdrawn: the entry stays, because its manifest still describes a real load.
    http.delete('/convoys/:id/vehicles/:vin', ({ params, request }) => {
      const id = idFrom(params['id']);
      const convoy = db.get(id);
      if (!convoy) {
        return new HttpResponse(null, { status: 404 });
      }
      if (convoy.arrived) {
        return problem(409, 'This convoy has already arrived.');
      }
      const vin = decodeURIComponent(String(params['vin']));
      const entry = entryFor(id, vin);
      if (!entry) {
        return problem(404, `There is no vehicle with VIN '${vin}' on this convoy.`);
      }

      if (!convoy.truckListPublished) {
        vehicles.set(
          id,
          (vehicles.get(id) ?? []).filter((v) => v.vin !== vin),
        );
        crew.delete(crewKey(id, vin));
        insurance.delete(crewKey(id, vin));
        return new HttpResponse(null, { status: 204 });
      }

      if (entry.withdrawn) {
        return problem(409, `Vehicle '${vin}' has already been withdrawn from this convoy.`);
      }
      const reason = new URL(request.url).searchParams.get('reason');
      vehicles.set(
        id,
        (vehicles.get(id) ?? []).map((v) =>
          v.vin === vin
            ? {
                ...v,
                withdrawnAt: '2026-09-03T14:30:00',
                withdrawnReason: reason,
                travelling: false,
                withdrawn: true,
              }
            : v,
        ),
      );
      return new HttpResponse(null, { status: 204 });
    }),

    // The same rules as ConvoyReadiness.Assess: two drivers *per leg* and insurance covering the
    // departure date per vehicle; a route and at least one vehicle still travelling for the
    // convoy. A withdrawn vehicle is skipped — it has no crew to find and no insurance to renew.
    http.get('/convoys/:id/readiness', ({ params }) => {
      const id = idFrom(params['id']);
      const convoy = db.get(id);
      if (!convoy) {
        return new HttpResponse(null, { status: 404 });
      }
      const departs = convoy.start.slice(0, 10);
      const legLabels = { Uk: 'UK to Europe', Border: 'Europe to Ukraine' } as const;
      const assessed = (vehicles.get(id) ?? [])
        .filter((vehicle) => !vehicle.withdrawn)
        .map((vehicle) => {
          const policy = insurance.get(crewKey(id, vehicle.vin));
          const insuranceProblem = !policy
            ? 'Insurance not recorded'
            : policy.voided
              ? 'Insurance voided by a crew change'
              : policy.coverStart.slice(0, 10) > departs || policy.coverEnd.slice(0, 10) < departs
                ? 'Insurance does not cover the departure date'
                : null;
          const legs = (['Uk', 'Border'] as const).map((leg) => {
            const drivers = leg === 'Uk' ? vehicle.ukDriverCount : vehicle.borderDriverCount;
            const legReasons =
              drivers >= 2 ? [] : [`Fewer than two drivers on the ${legLabels[leg]} leg`];
            return { leg, drivers, ready: legReasons.length === 0, reasons: legReasons };
          });
          const reasons = [
            ...legs.flatMap((leg) => leg.reasons),
            ...(insuranceProblem ? [insuranceProblem] : []),
          ];
          return {
            vin: vehicle.vin,
            plate: vehicle.plate,
            insured: insuranceProblem === null,
            ready: reasons.length === 0,
            legs,
            reasons,
          };
        });
      const routePlanned = (routes.get(id) ?? []).length > 0;
      const notReady = assessed.filter((vehicle) => !vehicle.ready).length;
      const reasons = [
        ...(routePlanned ? [] : ['No route planned']),
        ...(assessed.length > 0 ? [] : ['No vehicles on the truck list']),
        ...(notReady === 0
          ? []
          : [notReady === 1 ? '1 vehicle not ready' : `${String(notReady)} vehicles not ready`]),
      ];
      return HttpResponse.json({
        ready: reasons.length === 0,
        routePlanned,
        reasons,
        vehicles: assessed,
      });
    }),

    http.post('/convoys/:id/arrive', ({ params }) => {
      const id = idFrom(params['id']);
      const convoy = db.get(id);
      if (!convoy) {
        return new HttpResponse(null, { status: 404 });
      }
      if (convoy.arrived) {
        return problem(409, 'This convoy has already arrived.');
      }
      if (!convoy.truckListPublished) {
        return problem(
          409,
          "This convoy's truck list was never published, so it has not travelled.",
        );
      }
      const onIt = vehicles.get(id) ?? [];
      const travelling = onIt
        .filter((v) => !FINISHED.includes(manifestStatusByVin.get(v.vin) ?? 'Created'))
        .map((v) => v.vin);
      if (travelling.length > 0) {
        return problem(
          409,
          `These vehicles have no Delivered, Lost or Returned manifest yet: ${travelling.join(', ')}.`,
        );
      }
      const arrivedAt = '2026-09-05T17:00:00';
      db.set(id, { ...convoy, arrived: true, arrivedAt });
      for (const { vin } of onIt) {
        const vehicle = fleet.get(vin);
        if (!vehicle) {
          continue;
        }
        fleet.set(
          vin,
          manifestStatusByVin.get(vin) === 'Returned'
            ? { ...vehicle, convoyId: null }
            : { ...vehicle, handedOverAt: arrivedAt },
        );
      }
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

    http.get('/convoys/:id/vehicles/:vin/crew', ({ params, request }) => {
      const convoyId = idFrom(params['id']);
      const vin = decodeURIComponent(String(params['vin']));
      if (!db.has(convoyId) || !onConvoy(convoyId, vin)) {
        return new HttpResponse(null, { status: 404 });
      }
      const leg = new URL(request.url).searchParams.get('leg');
      const members = crew.get(crewKey(convoyId, vin)) ?? [];
      return HttpResponse.json(leg === null ? members : members.filter((m) => m.leg === leg));
    }),

    http.put('/convoys/:id/vehicles/:vin/crew/:personId', async ({ params, request }) => {
      const convoyId = idFrom(params['id']);
      if (!db.has(convoyId)) {
        return new HttpResponse(null, { status: 404 });
      }
      const vin = decodeURIComponent(String(params['vin']));
      const person = people.get(String(params['personId']));
      if (!person) {
        return problem(404, 'There is no volunteer with that ID.');
      }
      const body = crewBodySchema.safeParse(await request.json().catch(() => null));
      if (!body.success) {
        return validationProblem({
          Leg: ["'Leg' must be 'Uk' (UK to Europe) or 'Border' (Europe to Ukraine)."],
        });
      }
      const { leg } = body.data;
      const role = body.data.role ?? 'Driver';
      if (role === 'Driver' && !person.isDriver) {
        return problem(
          422,
          'That volunteer is not registered as a driver. They can ride as a passenger instead.',
        );
      }
      const entry = entryFor(convoyId, vin);
      if (!entry) {
        return problem(404, `There is no vehicle with VIN '${vin}' on this convoy.`);
      }
      if (entry.withdrawn) {
        return problem(
          409,
          `Vehicle '${vin}' has been withdrawn from this convoy, so its crew can no longer change.`,
        );
      }
      const key = crewKey(convoyId, vin);
      const members = crew.get(key) ?? [];
      if (members.some((m) => m.personId === person.id && m.leg === leg)) {
        return problem(409, 'That volunteer is already crewing this vehicle on this leg.');
      }
      // One seat per person per leg: they may change vehicle at the border, not mid-leg.
      const seatedElsewhere = [...crew.entries()].some(
        ([otherKey, others]) =>
          otherKey.startsWith(`${String(convoyId)}:`) &&
          otherKey !== key &&
          others.some((m) => m.personId === person.id && m.leg === leg),
      );
      if (seatedElsewhere) {
        return problem(
          409,
          'That volunteer is already crewing another vehicle on this leg. A person takes one seat per leg.',
        );
      }
      crew.set(key, [
        ...members,
        { personId: person.id, firstName: person.firstName, lastName: person.lastName, leg, role },
      ]);
      setCrewCounts(convoyId, vin);
      voidInsurance(convoyId, vin);
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/convoys/:id/vehicles/:vin/crew/:personId', ({ params, request }) => {
      const convoyId = idFrom(params['id']);
      const vin = decodeURIComponent(String(params['vin']));
      if (!db.has(convoyId) || !onConvoy(convoyId, vin)) {
        return new HttpResponse(null, { status: 404 });
      }
      const leg = new URL(request.url).searchParams.get('leg');
      const key = crewKey(convoyId, vin);
      const members = crew.get(key) ?? [];
      const next = members.filter(
        (m) => !(m.personId === String(params['personId']) && m.leg === leg),
      );
      if (next.length === members.length) {
        return new HttpResponse(null, { status: 404 });
      }
      crew.set(key, next);
      setCrewCounts(convoyId, vin);
      voidInsurance(convoyId, vin);
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/convoys/:id/vehicles/:vin/insurance', ({ params }) => {
      const policy = insurance.get(
        crewKey(idFrom(params['id']), decodeURIComponent(String(params['vin']))),
      );
      return policy ? HttpResponse.json(policy) : new HttpResponse(null, { status: 404 });
    }),

    http.put('/convoys/:id/vehicles/:vin/insurance', async ({ params, request }) => {
      const convoyId = idFrom(params['id']);
      const vin = decodeURIComponent(String(params['vin']));
      if (!db.has(convoyId)) {
        return new HttpResponse(null, { status: 404 });
      }
      if (!onConvoy(convoyId, vin)) {
        return problem(404, `There is no vehicle with VIN '${vin}' on this convoy.`);
      }
      const parsed = insuranceBodySchema.safeParse(await request.json());
      if (!parsed.success) {
        return validationProblem({ Insurer: [parsed.error.message] });
      }
      if (parsed.data.coverEnd < parsed.data.coverStart) {
        return validationProblem({ CoverEnd: ['Cover cannot end before it starts.'] });
      }
      insurance.set(crewKey(convoyId, vin), {
        convoyId,
        vin,
        insurer: parsed.data.insurer,
        policyNumber: parsed.data.policyNumber,
        coverStart: `${parsed.data.coverStart}T00:00:00`,
        coverEnd: `${parsed.data.coverEnd}T00:00:00`,
        costGbp: parsed.data.costGbp ?? null,
        recordedBy: 'test-user',
        recordedAt: '2026-08-24T12:00:00',
        voidedAt: null,
        voided: false,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/convoys/:id/vehicles/:vin/insurance', ({ params }) =>
      insurance.delete(crewKey(idFrom(params['id']), decodeURIComponent(String(params['vin']))))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),
  ];

  return { db, routes, vehicles, crew, insurance, handlers };
}
