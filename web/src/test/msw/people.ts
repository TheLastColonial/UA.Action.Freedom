import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { CreatePersonRequest, PersonReadModel } from '../../api/schemas/people';
import { problem } from './problem';

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

export interface PersonApiOptions {
  /** Volunteers on a live crew or manifest team, whom the API refuses to erase. */
  activeIds?: readonly string[];
}

export function personApi(
  seed: readonly PersonReadModel[] = [],
  { activeIds = [] }: PersonApiOptions = {},
): PersonApi {
  const db = new Map<string, PersonReadModel>(seed.map((p) => [p.id, p]));
  const active = new Set(activeIds);

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
      const id = String(params['id']);
      if (db.has(id) && active.has(id)) {
        return problem(
          409,
          'This volunteer is on the crew of a convoy that has not arrived, or on the team of a manifest still under way. Take them off it before erasing their details.',
        );
      }
      return db.delete(id)
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 });
    }),
  ];

  return { db, handlers };
}
