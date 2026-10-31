import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { BoxOutcome, BoxOutcomeReadModel, ConvoyReport } from '../../api/schemas/outcomes';
import { nextOutcomes } from '../../pages/convoys/outcomeModel';
import type { ConvoyApi } from './convoys';
import { problem } from './problem';

export interface OutcomesApi {
  /** Current outcomes, keyed `convoyId:boxId`. */
  outcomes: Map<string, BoxOutcomeReadModel>;
  /** The report served by GET /convoys/:id/report, keyed by convoy. */
  reports: Map<number, ConvoyReport>;
  /** Every body POSTed to an outcome route, so a test can assert what the screen sent. */
  recorded: { boxId: number; body: Record<string, unknown> }[];
  /** Goods lists accepted, `vin:receiverRef`. */
  accepted: string[];
  handlers: RequestHandler[];
}

const idFrom = (raw: unknown) => Number(raw);

/**
 * The outcome routes over a convoy fake, mirroring the rules the API enforces: a legal edge, a bounded reason, and the
 * convoy having departed. The vehicle and its boxes live in the convoy fake.
 */
export function outcomesApi(convoys: ConvoyApi): OutcomesApi {
  const outcomes = new Map<string, BoxOutcomeReadModel>();
  const reports = new Map<number, ConvoyReport>();
  const recorded: { boxId: number; body: Record<string, unknown> }[] = [];
  const accepted: string[] = [];

  const handlers: RequestHandler[] = [
    http.get('/convoys/:id/outcomes', ({ params }) => {
      const id = idFrom(params['id']);
      return HttpResponse.json([...outcomes.values()].filter((o) => o.convoyId === id));
    }),

    http.post('/convoys/:id/boxes/:boxId/outcome', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const boxId = idFrom(params['boxId']);
      const body = (await request.json()) as {
        outcome: BoxOutcome;
        reason?: string;
        hubLocationId?: number;
      };
      recorded.push({ boxId, body });

      const key = `${String(id)}:${String(boxId)}`;
      const current = outcomes.get(key);
      if (!nextOutcomes(current?.outcome).includes(body.outcome)) {
        return problem(409, 'That outcome cannot follow this box current outcome.', {
          type: 'box-outcome-illegal',
        });
      }
      const vin =
        current?.vin ??
        [...convoys.boxes.entries()]
          .find(([, rows]) => rows.some((b) => b.boxId === boxId))?.[0]
          .split(':')[1] ??
        '';
      outcomes.set(key, {
        boxId,
        convoyId: id,
        vin,
        outcome: body.outcome,
        reason: body.reason ?? null,
        hubLocationId: body.hubLocationId ?? null,
        recordedAt: '2026-03-04T12:00:00',
        recordedByName: null,
      });
      return HttpResponse.json({ boxId, outcome: body.outcome });
    }),

    http.post('/convoys/:id/vehicles/:vin/delivered', ({ params }) => {
      const id = idFrom(params['id']);
      const vin = decodeURIComponent(String(params['vin']));
      const rows = convoys.vehicles.get(id) ?? [];
      const entry = rows.find((v) => v.vin === vin);
      if (!entry) {
        return problem(404, `There is no vehicle with VIN '${vin}' on this convoy.`);
      }
      if (entry.delivered) {
        return problem(409, 'That vehicle has already been delivered.', {
          type: 'vehicle-already-delivered',
        });
      }
      convoys.vehicles.set(
        id,
        rows.map((v) =>
          v.vin === vin ? { ...v, delivered: true, deliveredAt: '2026-03-04T12:00:00' } : v,
        ),
      );
      const onBoard = (convoys.boxes.get(`${String(id)}:${vin}`) ?? []).filter(
        (b) => !outcomes.has(`${String(id)}:${String(b.boxId)}`),
      );
      for (const box of onBoard) {
        outcomes.set(`${String(id)}:${String(box.boxId)}`, {
          boxId: box.boxId,
          convoyId: id,
          vin,
          outcome: 'Delivered',
          reason: null,
          hubLocationId: null,
          recordedAt: '2026-03-04T12:00:00',
          recordedByName: null,
        });
      }
      return HttpResponse.json({ boxesDelivered: onBoard.length });
    }),

    http.post(
      '/convoys/:id/vehicles/:vin/declarations/goods-list/:receiverRef/accepted',
      ({ params }) => {
        accepted.push(
          `${decodeURIComponent(String(params['vin']))}:${String(params['receiverRef'])}`,
        );
        return HttpResponse.json({ boxesAccepted: 1 });
      },
    ),

    http.post('/convoys/:id/close', ({ params }) => {
      const id = idFrom(params['id']);
      const convoy = convoys.db.get(id);
      if (!convoy?.arrived) {
        return problem(409, 'A convoy can only be closed after it has arrived.', {
          type: 'convoy-not-arrived',
        });
      }
      convoys.db.set(id, { ...convoy, closed: true, closedAt: '2026-03-08T09:00:00' });
      const report = reports.get(id);
      return report ? HttpResponse.json(report) : new HttpResponse(null, { status: 404 });
    }),

    http.get('/convoys/:id/report', ({ params }) => {
      const report = reports.get(idFrom(params['id']));
      return report ? HttpResponse.json(report) : new HttpResponse(null, { status: 404 });
    }),
  ];

  return { outcomes, reports, recorded, accepted, handlers };
}
