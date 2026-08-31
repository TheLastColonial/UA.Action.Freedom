import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { ReceiverDetailReadModel, SetReceiverDetailRequest } from '../../api/receiverDetail';
import type { CreateReceiverRequest, ReceiverReadModel } from '../../api/schemas/receivers';

export interface ReceiverAccess {
  ref: string;
  reason: string | null;
}

export interface ReceiverApi {
  db: Map<string, ReceiverReadModel>;
  details: Map<string, ReceiverDetailReadModel>;
  accessLog: ReceiverAccess[];
  handlers: RequestHandler[];
}

let minted = 0;

export function receiverApi(
  seed: readonly ReceiverReadModel[] = [],
  seedDetails: readonly ReceiverDetailReadModel[] = [],
): ReceiverApi {
  const db = new Map<string, ReceiverReadModel>(seed.map((r) => [r.ref, r]));
  const details = new Map<string, ReceiverDetailReadModel>(seedDetails.map((d) => [d.ref, d]));
  const accessLog: ReceiverAccess[] = [];
  const refOf = (raw: string | readonly string[] | undefined) => decodeURIComponent(String(raw));

  const handlers: RequestHandler[] = [
    http.get('/receivers', () => HttpResponse.json([...db.values()])),

    http.get('/receivers/:ref', ({ params }) => {
      const receiver = db.get(refOf(params['ref']));
      return receiver ? HttpResponse.json(receiver) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/receivers', async ({ request }) => {
      minted += 1;
      const ref = `dddddddd-0000-0000-0000-${String(minted).padStart(12, '0')}`;
      const body = (await request.json()) as CreateReceiverRequest;
      db.set(ref, { ref, organisation: body.organisation, region: body.region });
      return new HttpResponse(null, { status: 201, headers: { Location: `/receivers/${ref}` } });
    }),

    http.put('/receivers/:ref', async ({ params, request }) => {
      const ref = refOf(params['ref']);
      if (!db.has(ref)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as CreateReceiverRequest;
      db.set(ref, { ref, organisation: body.organisation, region: body.region });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/receivers/:ref', ({ params }) => {
      const ref = refOf(params['ref']);
      if (!db.delete(ref)) {
        return new HttpResponse(null, { status: 404 });
      }
      details.delete(ref);
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/receivers/:ref/detail', ({ params, request }) => {
      const ref = refOf(params['ref']);
      const reason = new URL(request.url).searchParams.get('reason');
      // The API audits the attempt whether or not detail is found.
      accessLog.push({ ref, reason });
      const detail = details.get(ref);
      return detail ? HttpResponse.json(detail) : new HttpResponse(null, { status: 404 });
    }),

    http.put('/receivers/:ref/detail', async ({ params, request }) => {
      const ref = refOf(params['ref']);
      if (!db.has(ref)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as SetReceiverDetailRequest;
      details.set(ref, {
        ref,
        contactName: body.contactName,
        contactPhone: body.contactPhone,
        addressLine1: body.addressLine1,
        addressLine2: body.addressLine2 ?? null,
        city: body.city,
        postCode: body.postCode ?? null,
        deleteAfter: body.deleteAfter ?? null,
      });
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { db, details, accessLog, handlers };
}
