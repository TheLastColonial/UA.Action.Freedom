import { SEED_USERS } from './authFiles';
import type { SeedUser } from './authFiles';

const apiUrl = process.env['PLAYWRIGHT_BASE_URL'] ?? 'http://localhost:8080';
const tokenUrl = `${(process.env['FREEDOM_OIDC_URL'] ?? 'http://localhost:8081/realms/freedom').replace(/\/$/, '')}/protocol/openid-connect/token`;
const clientId = process.env['FREEDOM_OIDC_CLIENT_ID'] ?? 'freedom-app';
const clientSecret = process.env['FREEDOM_OIDC_CLIENT_SECRET'] ?? 'local-freedom-client-secret';
const password = process.env['FREEDOM_TEST_PASSWORD'] ?? 'password';

interface Me {
  subject: string | null;
  personId: string | null;
}

interface Person {
  id: string;
  firstName: string;
  lastName: string;
}

async function tokenFor(user: SeedUser): Promise<string> {
  const response = await fetch(tokenUrl, {
    method: 'POST',
    body: new URLSearchParams({
      grant_type: 'password',
      client_id: clientId,
      client_secret: clientSecret,
      username: user,
      password,
      scope: 'openid',
    }),
  });
  const body = (await response.json()) as { access_token: string };
  return body.access_token;
}

async function call(path: string, token: string, init: RequestInit = {}): Promise<Response> {
  return fetch(`${apiUrl}${path}`, {
    ...init,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
  });
}

async function volunteerFor(user: SeedUser, adminToken: string): Promise<string> {
  const listed = (await (await call('/people?pageSize=200', adminToken)).json()) as Person[];
  const existing = listed.find((p) => p.firstName === 'E2E' && p.lastName === user);
  if (existing) return existing.id;

  const created = await call('/people', adminToken, {
    method: 'POST',
    body: JSON.stringify({
      firstName: 'E2E',
      lastName: user,
      dateOfBirth: '1990-01-01T00:00:00Z',
      joined: '2024-01-01T00:00:00Z',
      isDriver: false,
      committed: false,
    }),
  });
  return created.headers.get('Location')?.split('/').pop() ?? '';
}

/**
 * Links every seed login to a volunteer, so a spec that validates or shelves a box (signed as
 * the caller) has a caller who can sign. Keycloak generates the subjects when the realm is
 * imported, so this reads them from `GET /me` on every run rather than assuming ids.
 */
export async function linkSeedLogins(): Promise<void> {
  const adminToken = await tokenFor('admin');

  for (const user of SEED_USERS) {
    const token = await tokenFor(user);
    const me = (await (await call('/me', token)).json()) as Me;
    if (me.personId !== null || me.subject === null) continue;

    const personId = await volunteerFor(user, adminToken);
    await call(`/people/${personId}/login`, adminToken, {
      method: 'PUT',
      body: JSON.stringify({ subject: me.subject }),
    });
  }
}
