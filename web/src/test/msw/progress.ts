import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { ConvoyProgress, ProgressPoint, RoutePointMark } from '../../api/schemas/progress';
import { problem } from './problem';

export function makeProgressPoint(overrides: Partial<ProgressPoint> = {}): ProgressPoint {
  return {
    routePointId: 1,
    sequence: 1,
    name: 'Coventry hub',
    kind: 'Hub',
    authority: null,
    marks: [],
    accommodation: [],
    ...overrides,
  };
}

export function makeProgress(overrides: Partial<ConvoyProgress> = {}): ConvoyProgress {
  return {
    convoyId: 7,
    departedAt: '2026-03-01T08:00:00',
    arrivedAt: null,
    points: [],
    vehicles: [],
    hubs: [],
    hqPhone: null,
    ...overrides,
  };
}

export interface ProgressApi {
  progress: ConvoyProgress;
  /** Every mark POSTed, `reached` or `crossed`, so a test can assert what the page sent. */
  posted: { pointId: number; verb: 'reached' | 'crossed'; body: Record<string, unknown> }[];
  /** Every fuel cost POSTed. */
  fuel: Record<string, unknown>[];
  /** The mark the API will say a Dispatcher made, by name, when `actingAs` is set. */
  handlers: RequestHandler[];
}

/** GET /convoys/:id/progress and the two mark routes, with the rules the API enforces (a crossing is a border's alone). */
export function progressApi(
  initial: ConvoyProgress,
  options: { recordedByName?: string; onBehalfOfLeader?: boolean } = {},
): ProgressApi {
  const state: ProgressApi = { progress: initial, posted: [], fuel: [], handlers: [] };

  const mark = (verb: 'reached' | 'crossed') =>
    http.post(`/convoys/:id/route/:pointId/${verb}`, async ({ params, request }) => {
      const pointId = Number(params['pointId']);
      const body = (await request.json().catch(() => ({}))) as Record<string, unknown>;
      state.posted.push({ pointId, verb, body });

      const point = state.progress.points.find((p) => p.routePointId === pointId);
      if (!point) {
        return problem(404, 'That point is not on this convoy route.');
      }
      if (verb === 'crossed' && point.kind !== 'Border') {
        return problem(422, 'Only a border can be crossed.', { type: 'crossing-needs-border' });
      }
      const recorded: RoutePointMark = {
        routePointId: pointId,
        kind: verb === 'reached' ? 'Reached' : 'Crossed',
        vin: null,
        recordedByName: options.recordedByName ?? 'Lena Leader',
        recordedAt: '2026-03-02T14:30:00',
        onBehalfOfLeader: options.onBehalfOfLeader ?? false,
      };
      state.progress = {
        ...state.progress,
        points: state.progress.points.map((p) =>
          p.routePointId === pointId ? { ...p, marks: [...p.marks, recorded] } : p,
        ),
      };
      return new HttpResponse(null, { status: 204 });
    });

  state.handlers = [
    http.get('/convoys/:id/progress', () => HttpResponse.json(state.progress)),
    mark('reached'),
    mark('crossed'),
    http.post('/convoys/:id/costs', async ({ request }) => {
      state.fuel.push((await request.json()) as Record<string, unknown>);
      return HttpResponse.json(
        { id: 1 },
        { status: 201, headers: { Location: '/convoys/7/costs/1' } },
      );
    }),
  ];

  return state;
}
