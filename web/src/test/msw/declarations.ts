import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import { KIND_SEGMENT, REFUSAL_REASONS } from '../../api/schemas/declarations';
import type {
  DeclarationKind,
  DeclarationReadModel,
  EnsDeclarationReadModel,
  RecordDeclarationRequest,
  RecordEnsRequest,
  RedeclareTask,
  RefuseDeclarationRequest,
} from '../../api/schemas/declarations';
import { problem } from './problem';

export interface DeclarationApiOptions {
  /** Whether filing goes through a worker. The API answers 409 "record the reference" otherwise. */
  automatic?: boolean;
}

export interface DeclarationApi {
  rows: DeclarationReadModel[];
  ens: Map<number, EnsDeclarationReadModel>;
  handlers: RequestHandler[];
}

const RESOLUTION: Record<DeclarationKind, RedeclareTask['resolution']> = {
  Gmr: 'UpdateOrRecreate',
  Ens: 'InvalidateAndRefile',
  Elo: 'NewEnvelopeAgainstNewMrn',
  GoodsList: 'PrepareNewListAndHoldAtHub',
};

const BASE = '/convoys/:convoyId/vehicles/:vin/declarations';

const KIND_BY_SEGMENT = new Map<string, DeclarationKind>(
  (Object.entries(KIND_SEGMENT) as [DeclarationKind, string][]).map(([kind, segment]) => [
    segment,
    kind,
  ]),
);

function str(value: string | readonly string[] | undefined): string {
  return typeof value === 'string' ? value : (value?.[0] ?? '');
}

/**
 * An in-memory stand-in for the declarations routes. It enforces what the API does: a reference is
 * write-once, only a filed declaration can be refused, the ELO needs an accepted ENS, a goods list
 * needs its receiver, and a withdrawn ENS is kept.
 */
export function declarationApi(options: DeclarationApiOptions = {}): DeclarationApi {
  const rows: DeclarationReadModel[] = [];
  const ens = new Map<number, EnsDeclarationReadModel>();
  let nextId = 1;

  const row = (
    convoyId: number,
    vin: string,
    kind: DeclarationKind,
    status: DeclarationReadModel['status'],
    reference: string | null,
    receiverRef: string | null,
  ): DeclarationReadModel => ({
    id: nextId++,
    convoyId,
    vin,
    kind,
    status,
    receiverRef,
    reference,
    reasonCode: null,
    recordedByName: null,
    recordedAt: null,
    lastChangedByName: null,
    lastChangedAt: null,
  });

  const currentIndex = (
    convoyId: number,
    vin: string,
    kind: DeclarationKind,
    receiverRef: string | null,
  ) =>
    rows.findIndex(
      (declaration) =>
        declaration.convoyId === convoyId &&
        declaration.vin === vin &&
        declaration.kind === kind &&
        declaration.receiverRef === receiverRef &&
        declaration.status !== 'Withdrawn',
    );

  const ensAccepted = (convoyId: number, vin: string) =>
    currentIndex(convoyId, vin, 'Ens', null) >= 0;

  const handlers: RequestHandler[] = [
    http.get(BASE, ({ params }) =>
      HttpResponse.json(
        rows.filter(
          (declaration) =>
            declaration.convoyId === Number(params['convoyId']) &&
            declaration.vin === str(params['vin']),
        ),
      ),
    ),

    http.get(`${BASE}/ens`, ({ params }) => {
      const index = currentIndex(Number(params['convoyId']), str(params['vin']), 'Ens', null);
      const declaration = index < 0 ? undefined : rows[index];
      const detail = declaration ? ens.get(declaration.id) : undefined;
      return detail ? HttpResponse.json(detail) : new HttpResponse(null, { status: 404 });
    }),

    http.put(`${BASE}/ens`, async ({ params, request }) => {
      const convoyId = Number(params['convoyId']);
      const vin = str(params['vin']);
      if (ensAccepted(convoyId, vin)) {
        return problem(
          409,
          'This vehicle already has an ICS2 declaration recorded. A declaration is write-once, because the logistics envelope names it: withdraw it first.',
        );
      }
      const body = (await request.json()) as RecordEnsRequest;
      const declaration = row(convoyId, vin, 'Ens', 'Accepted', body.mrn, null);
      rows.push(declaration);
      ens.set(declaration.id, {
        declarationId: declaration.id,
        mrn: body.mrn,
        acceptedAt: body.acceptedAt,
        filedBy: body.filedBy,
        filingReference: body.filingReference ?? null,
      });
      return new HttpResponse(null, { status: 201 });
    }),

    http.delete(`${BASE}/ens`, ({ params }) => {
      const index = currentIndex(Number(params['convoyId']), str(params['vin']), 'Ens', null);
      const declaration = index < 0 ? undefined : rows[index];
      if (!declaration) {
        return new HttpResponse(null, { status: 404 });
      }
      rows[index] = { ...declaration, status: 'Withdrawn' };
      return new HttpResponse(null, { status: 204 });
    }),

    http.post(`${BASE}/:kind/record`, async ({ params, request }) => {
      const convoyId = Number(params['convoyId']);
      const vin = str(params['vin']);
      const kind = KIND_BY_SEGMENT.get(str(params['kind']));
      const body = (await request.json()) as RecordDeclarationRequest;
      if (kind === undefined) {
        return problem(400, 'The kind must be one of: gmr, ens, elo, goods-list.');
      }
      if (kind === 'Ens') {
        return problem(400, 'An ENS carries more than a reference. Use PUT .../declarations/ens.');
      }
      if (kind === 'GoodsList' && !body.receiverRef) {
        return problem(
          400,
          'A Ukrainian goods list is one per receiver, so the receiver is required.',
        );
      }
      if (kind === 'Elo' && !ensAccepted(convoyId, vin)) {
        return problem(
          409,
          'The ELO is created from the ENS MRN, so this vehicle needs an accepted ENS before its envelope can be recorded.',
        );
      }
      const receiverRef = body.receiverRef ?? null;
      const index = currentIndex(convoyId, vin, kind, receiverRef);
      const existing = index < 0 ? undefined : rows[index];
      if (existing && existing.reference !== null && existing.status !== 'Refused') {
        return problem(
          409,
          'This declaration already carries a reference. A reference is write-once.',
        );
      }
      if (existing) {
        rows[index] = { ...existing, status: 'Filed', reference: body.reference, reasonCode: null };
      } else {
        rows.push(row(convoyId, vin, kind, 'Filed', body.reference, receiverRef));
      }
      return new HttpResponse(null, { status: 204 });
    }),

    http.post(`${BASE}/:kind/refused`, async ({ params, request }) => {
      const kind = KIND_BY_SEGMENT.get(str(params['kind']));
      const body = (await request.json()) as RefuseDeclarationRequest;
      if (kind === undefined || !REFUSAL_REASONS.includes(body.reasonCode)) {
        return problem(400, 'That is not a known refusal reason.');
      }
      const index = currentIndex(
        Number(params['convoyId']),
        str(params['vin']),
        kind,
        body.receiverRef ?? null,
      );
      const existing = index < 0 ? undefined : rows[index];
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      if (existing.status !== 'Filed') {
        return problem(409, 'Only a filed declaration can be refused.');
      }
      rows[index] = { ...existing, status: 'Refused', reasonCode: body.reasonCode };
      return new HttpResponse(null, { status: 204 });
    }),

    http.post(`${BASE}/:declarationId/withdraw`, ({ params }) => {
      const id = Number(params['declarationId']);
      const index = rows.findIndex((declaration) => declaration.id === id);
      const existing = index < 0 ? undefined : rows[index];
      if (!existing) {
        return new HttpResponse(null, { status: 404 });
      }
      if (existing.status !== 'Stale') {
        return problem(
          409,
          'Only a stale declaration can be withdrawn: this one still matches the load it was written from.',
        );
      }
      rows[index] = { ...existing, status: 'Withdrawn' };
      rows.push(
        row(existing.convoyId, existing.vin, existing.kind, 'Draft', null, existing.receiverRef),
      );
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/convoys/:convoyId/tasks', ({ params }) =>
      HttpResponse.json(
        rows
          .filter(
            (declaration) =>
              declaration.convoyId === Number(params['convoyId']) && declaration.status === 'Stale',
          )
          .map((declaration) => ({
            type: 'redeclare',
            declarationId: declaration.id,
            vin: declaration.vin,
            kind: declaration.kind,
            receiverRef: declaration.receiverRef,
            reference: declaration.reference,
            resolution: RESOLUTION[declaration.kind],
          })),
      ),
    ),

    http.post(`${BASE}/:kind/file`, ({ params }) => {
      const convoyId = Number(params['convoyId']);
      const vin = str(params['vin']);
      const kind = KIND_BY_SEGMENT.get(str(params['kind']));
      if (kind !== 'Gmr' && kind !== 'Elo') {
        return problem(
          409,
          'Only the GMR and the ELO have a client. The ENS and the Ukrainian goods list are filed by hand: record the reference instead.',
        );
      }
      if (!options.automatic) {
        return problem(
          409,
          "This authority's submission mode is manual: record the reference instead, with POST .../declarations/{kind}/record.",
        );
      }
      if (kind === 'Elo' && !ensAccepted(convoyId, vin)) {
        return problem(
          409,
          'The ELO is created from the ENS MRN, so this vehicle needs an accepted ENS first.',
        );
      }
      rows.push(row(convoyId, vin, kind, 'Filed', null, null));
      return new HttpResponse(null, { status: 202 });
    }),
  ];

  return { rows, ens, handlers };
}
