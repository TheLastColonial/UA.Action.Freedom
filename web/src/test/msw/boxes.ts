import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  AddBoxItemRequest,
  AssignBoxBayRequest,
  BoxBayAssignmentReadModel,
  BoxItemReadModel,
  BoxReadModel,
  CreateBoxRequest,
  ValidateBoxRequest,
} from '../../api/schemas/boxes';
import type { ItemCategoryReadModel } from '../../api/schemas/categories';
import type { ReceiverStatus } from '../../api/schemas/receivers';
import { problem } from './problem';

interface ActiveQrCode {
  token: string;
  issuedAt: string;
}

export interface BoxApi {
  db: Map<number, BoxReadModel>;
  items: Map<number, BoxItemReadModel[]>;
  qr: Map<number, ActiveQrCode>;
  bayHistory: Map<number, BoxBayAssignmentReadModel[]>;
  handlers: RequestHandler[];
}

let mintedBox = 500;
let mintedItem = 0;
let mintedToken = 0;
let mintedBayAssignment = 0;

function shelfLifeOf(
  expiresOn: string | null,
  warnWithinDays: number | null,
): BoxItemReadModel['shelfLife'] {
  if (expiresOn === null) {
    return 'Fine';
  }
  const today = new Date().toISOString().slice(0, 10);
  if (expiresOn < today) {
    return 'Expired';
  }
  if (warnWithinDays === null) {
    return 'Fine';
  }
  const limit = new Date(Date.now() + warnWithinDays * 86_400_000).toISOString().slice(0, 10);
  return expiresOn <= limit ? 'Short' : 'Fine';
}

export function boxApi(
  seed: readonly BoxReadModel[] = [],
  /** The volunteer the API resolves the caller's login to; what an attestation is signed as. */
  signedBy = 'caller-person-id',
  /**
   * The status of each receiver a box may be addressed to. When given, only a Registered one is
   * accepted — an unknown one is a 422 and any other status a 409, as the API answers.
   */
  receiverStatuses?: ReadonlyMap<string, ReceiverStatus>,
  /** The categories an item may be packed under; an item names one, as the API requires. */
  categories: readonly ItemCategoryReadModel[] = [],
): BoxApi {
  const db = new Map<number, BoxReadModel>(seed.map((b) => [b.id, b]));
  const items = new Map<number, BoxItemReadModel[]>();
  const qr = new Map<number, ActiveQrCode>();
  const bayHistory = new Map<number, BoxBayAssignmentReadModel[]>();
  const idFrom = (raw: string | readonly string[] | undefined) => Number(String(raw));
  const activeBay = (id: number) => (bayHistory.get(id) ?? []).find((a) => a.active);

  const receiverGuard = (receiverRef: string | null | undefined) => {
    if (!receiverStatuses || !receiverRef) {
      return null;
    }
    const status = receiverStatuses.get(receiverRef);
    if (status === undefined) {
      return problem(422, 'The receiver named does not exist.');
    }
    return status === 'Registered'
      ? null
      : problem(409, 'Only a registered receiver can be a destination.');
  };

  const validatedGuard = (box: BoxReadModel | undefined) =>
    box?.validated
      ? problem(409, 'This box has been validated and can no longer be changed.')
      : null;

  const handlers: RequestHandler[] = [
    http.get('/boxes', () => HttpResponse.json([...db.values()])),

    http.get('/boxes/:id', ({ params }) => {
      const box = db.get(idFrom(params['id']));
      return box ? HttpResponse.json(box) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/boxes', async ({ request }) => {
      mintedBox += 1;
      const body = (await request.json()) as CreateBoxRequest;
      const refused = receiverGuard(body.receiverRef);
      if (refused) {
        return refused;
      }
      db.set(mintedBox, {
        id: mintedBox,
        lastChangedByName: null,
        lastChangedAt: null,
        weightKg: 0,
        widthCm: null,
        depthCm: null,
        heightCm: null,
        receiverRef: body.receiverRef ?? null,
        locationId: body.locationId ?? null,
        validatedByPersonId: null,
        validatedAt: null,
        validated: false,
        voided: false,
        voidedAt: null,
        replacesBoxId: null,
        replacedByBoxId: null,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/boxes/${String(mintedBox)}` },
      });
    }),

    http.post('/boxes/:id/replace', ({ params }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      if (box.voided) {
        return problem(409, 'This box has already been replaced.');
      }
      if (!box.validated) {
        return problem(409, 'Only a validated box is replaced. Edit this one instead.');
      }
      // Mirrors the API: void the old box, drop its label, copy the items, name the one it replaces.
      mintedBox += 1;
      const replacementId = mintedBox;
      db.set(id, {
        ...box,
        voided: true,
        voidedAt: '2026-05-02T09:00:00',
        replacedByBoxId: replacementId,
      });
      qr.delete(id);
      db.set(replacementId, {
        ...box,
        id: replacementId,
        weightKg: 0,
        widthCm: null,
        depthCm: null,
        heightCm: null,
        validatedByPersonId: null,
        validatedAt: null,
        validated: false,
        voided: false,
        voidedAt: null,
        replacesBoxId: id,
        replacedByBoxId: null,
      });
      items.set(
        replacementId,
        (items.get(id) ?? []).map((item) => ({ ...item, id: `${item.id}-copy` })),
      );
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/boxes/${String(replacementId)}` },
      });
    }),

    http.put('/boxes/:id', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const frozen = validatedGuard(box);
      if (frozen) {
        return frozen;
      }
      const body = (await request.json()) as CreateBoxRequest;
      if (body.receiverRef !== box.receiverRef) {
        const refused = receiverGuard(body.receiverRef);
        if (refused) {
          return refused;
        }
      }
      db.set(id, {
        ...box,
        receiverRef: body.receiverRef ?? null,
        locationId: body.locationId ?? null,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/boxes/:id', ({ params }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      if (box.validated) {
        return problem(409, 'This box has been validated and can no longer be changed.');
      }
      db.delete(id);
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/boxes/:id/items', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(items.get(id) ?? []);
    }),

    http.post('/boxes/:id/items', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const frozen = validatedGuard(box);
      if (frozen) {
        return frozen;
      }
      const body = (await request.json()) as AddBoxItemRequest;
      const category = categories.find((c) => c.id === body.categoryId);
      if (!category) {
        return problem(422, 'The category named does not exist.', { type: 'category-not-found' });
      }
      mintedItem += 1;
      const itemId = `bbbbbbbb-0000-0000-0000-${String(mintedItem).padStart(12, '0')}`;
      const shelfLife = shelfLifeOf(body.expiresOn ?? null, category.warnWithinDays);
      const list = items.get(id) ?? [];
      list.push({
        id: itemId,
        description: body.description,
        properties: body.properties,
        categoryId: body.categoryId,
        commodityCode: body.commodityCode ?? null,
        quantity: body.quantity ?? null,
        valueGbp: body.valueGbp ?? null,
        valueSource: body.valueSource ?? null,
        expiresOn: body.expiresOn ?? null,
        categoryNameEn: category.nameEn,
        isNotCarried: category.isNotCarried,
        shelfLife,
        donationId: body.donationId ?? null,
      });
      items.set(id, list);
      const warnings = [
        ...(category.isNotCarried ? ['NotCarried'] : []),
        ...(shelfLife === 'Expired' ? ['Expired'] : []),
        ...(shelfLife === 'Short' ? ['ShortShelfLife'] : []),
      ];
      return HttpResponse.json({ itemId, warnings });
    }),

    http.delete('/boxes/:id/items/:itemId', ({ params }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const frozen = validatedGuard(box);
      if (frozen) {
        return frozen;
      }
      const itemId = String(params['itemId']);
      const list = items.get(id) ?? [];
      const next = list.filter((i) => i.id !== itemId);
      if (next.length === list.length) {
        return new HttpResponse(null, { status: 404 });
      }
      items.set(id, next);
      return new HttpResponse(null, { status: 204 });
    }),

    http.post('/boxes/:id/validate', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      if (box.validated) {
        return problem(409, 'This box has already been validated.');
      }
      if ((items.get(id) ?? []).some((item) => item.shelfLife === 'Expired')) {
        return problem(409, 'Take the expired item out of the box before it is validated.', {
          type: 'box-has-expired-items',
        });
      }
      const body = (await request.json()) as ValidateBoxRequest;
      db.set(id, {
        ...box,
        validated: true,
        validatedByPersonId: signedBy,
        validatedAt: '2026-04-01T00:00:00',
        weightKg: body.weightKg,
        widthCm: body.widthCm ?? null,
        depthCm: body.depthCm ?? null,
        heightCm: body.heightCm ?? null,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/boxes/:id/qr-code', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const code = qr.get(id);
      return code
        ? HttpResponse.json({
            token: code.token,
            boxId: id,
            issuedAt: code.issuedAt,
            revokedAt: null,
            active: true,
          })
        : new HttpResponse(null, { status: 404 });
    }),

    http.post('/boxes/:id/qr-code', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      if (db.get(id)?.voided) {
        return problem(409, 'A voided box takes no label.');
      }
      // Re-issuing replaces the active code — the previous token stops resolving.
      mintedToken += 1;
      const token = `cccccccc-0000-0000-0000-${String(mintedToken).padStart(12, '0')}`;
      qr.set(id, { token, issuedAt: '2026-05-01T09:00:00' });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/boxes/scan/${token}` },
      });
    }),

    http.delete('/boxes/:id/qr-code', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id) || !qr.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      qr.delete(id);
      return new HttpResponse(null, { status: 204 });
    }),

    http.get('/boxes/:id/label', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      if (!qr.has(id)) {
        return problem(409, 'This box has no QR code. Issue one before printing a label.');
      }
      // Mirrors the real label: the box number, the contents and the signer, never the destination.
      const svg =
        `<svg xmlns="http://www.w3.org/2000/svg" width="560" height="300">` +
        `<text x="248" y="52">UKRAINIAN ACTION</text>` +
        `<text x="248" y="112">BOX #${String(id)}</text>` +
        `<text x="16" y="270">Contents / Вміст</text>` +
        `</svg>`;
      return new HttpResponse(svg, {
        status: 200,
        headers: { 'Content-Type': 'image/svg+xml' },
      });
    }),

    http.get('/boxes/:id/bay', ({ params }) => {
      const id = idFrom(params['id']);
      if (!db.has(id)) {
        return new HttpResponse(null, { status: 404 });
      }
      const active = activeBay(id);
      return active ? HttpResponse.json(active) : new HttpResponse(null, { status: 404 });
    }),

    http.get('/boxes/:id/bay/history', ({ params }) => {
      const id = idFrom(params['id']);
      return HttpResponse.json(bayHistory.get(id) ?? []);
    }),

    http.put('/boxes/:id/bay', async ({ params, request }) => {
      const id = idFrom(params['id']);
      const box = db.get(id);
      if (!box) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as AssignBoxBayRequest;
      const now = '2026-05-01T09:00:00';
      const history = (bayHistory.get(id) ?? []).map((a) =>
        a.active ? { ...a, vacatedAt: now, active: false } : a,
      );
      mintedBayAssignment += 1;
      history.push({
        id: mintedBayAssignment,
        boxId: id,
        bayId: body.bayId,
        assignedByPersonId: signedBy,
        assignedAt: now,
        vacatedAt: null,
        active: true,
      });
      bayHistory.set(id, history);
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/boxes/:id/bay', ({ params }) => {
      const id = idFrom(params['id']);
      const active = activeBay(id);
      if (!active) {
        return new HttpResponse(null, { status: 404 });
      }
      const now = '2026-05-01T09:00:00';
      bayHistory.set(
        id,
        (bayHistory.get(id) ?? []).map((a) =>
          a.id === active.id ? { ...a, vacatedAt: now, active: false } : a,
        ),
      );
      return new HttpResponse(null, { status: 204 });
    }),
  ];

  return { db, items, qr, bayHistory, handlers };
}
