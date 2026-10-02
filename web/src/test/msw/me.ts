import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type { Me } from '../../api/schemas/me';

/** GET /me. Defaults to a login linked to a volunteer; pass `personId: null` for an unlinked one. */
export function meApi(overrides: Partial<Me> = {}): RequestHandler[] {
  const me: Me = {
    subject: 'kc-subject',
    roles: [],
    personId: 'caller-person-id',
    displayName: 'Val Checker',
    ...overrides,
  };

  return [http.get('/me', () => HttpResponse.json(me))];
}
