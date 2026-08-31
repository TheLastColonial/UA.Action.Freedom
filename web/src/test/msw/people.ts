import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { CreatePersonRequest, PersonReadModel } from '../../api/schemas/people';

export interface PersonApi {
  db: Map<string, PersonReadModel>;
  handlers: RequestHandler[];
}

let minted = 0;

function toReadModel(id: string, body: CreatePersonRequest): PersonReadModel {
  return {
    id,
    firstName: body.firstName,
    lastName: body.lastName,
    dateOfBirth: `${body.dateOfBirth}T00:00:00`,
    joined: `${body.joined}T00:00:00`,
    phone: body.phone ?? null,
    isDriver: body.isDriver,
    committed: body.committed,
  };
}

export function personApi(seed: readonly PersonReadModel[] = []): PersonApi {
  const db = new Map<string, PersonReadModel>(seed.map((p) => [p.id, p]));

  const handlers: RequestHandler[] = [
    http.get('/people', ({ request }) => {
      const url = new URL(request.url);
      const driversOnly = url.searchParams.get('driversOnly') === 'true';
      const rows = [...db.values()].filter((p) => !driversOnly || p.isDriver);
      return HttpResponse.json(rows);
    }),

    http.get('/people/:id', ({ params }) => {
      const person = db.get(String(params['id']));
      return person ? HttpResponse.json(person) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/people', async ({ request }) => {
      minted += 1;
      const id = `11111111-0000-0000-0000-${String(minted).padStart(12, '0')}`;
      const body = (await request.json()) as CreatePersonRequest;
      db.set(id, toReadModel(id, body));
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/people/${id}` },
      });
    }),

    http.put('/people/:id', async ({ params, request }) => {
      const id = String(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as CreatePersonRequest;
      db.set(id, toReadModel(id, body));
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/people/:id', ({ params }) => {
      return db.delete(String(params['id']))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 });
    }),
  ];

  return { db, handlers };
}
