import type { SeedUser } from './authFiles';

const apiUrl = process.env['PLAYWRIGHT_BASE_URL'] ?? 'http://localhost:8080';
const tokenUrl = `${(process.env['FREEDOM_OIDC_URL'] ?? 'http://localhost:8081/realms/freedom').replace(/\/$/, '')}/protocol/openid-connect/token`;
const clientId = process.env['FREEDOM_OIDC_CLIENT_ID'] ?? 'freedom-app';
const clientSecret = process.env['FREEDOM_OIDC_CLIENT_SECRET'] ?? 'local-freedom-client-secret';
const password = process.env['FREEDOM_TEST_PASSWORD'] ?? 'password';

type Login = SeedUser | 'operator' | 'leader';

async function tokenFor(user: Login): Promise<string> {
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
  return ((await response.json()) as { access_token: string }).access_token;
}

async function call(
  path: string,
  token: string,
  method: string,
  body?: unknown,
): Promise<Response> {
  const response = await fetch(`${apiUrl}${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`${method} ${path} -> ${String(response.status)} ${await response.text()}`);
  }
  return response;
}

const idFrom = (response: Response) => response.headers.get('Location')?.split('/').pop() ?? '';

/** The volunteer a login is linked to, linking one first if nobody has (Keycloak makes the subjects at import). */
async function volunteerOf(login: Login, adminToken: string): Promise<string> {
  const me = (await (await call('/me', await tokenFor(login), 'GET')).json()) as {
    subject: string;
    personId: string | null;
  };
  if (me.personId) return me.personId;

  const created = await call('/people', adminToken, 'POST', {
    firstName: 'E2E',
    lastName: login,
    dateOfBirth: '1990-01-01T00:00:00Z',
    joined: '2024-01-01T00:00:00Z',
    isDriver: true,
    committed: true,
  });
  const personId = idFrom(created);
  await call(`/people/${personId}/login`, adminToken, 'PUT', { subject: me.subject });
  return personId;
}

/**
 * A convoy that has departed, led by the volunteer behind <paramref name="leaderLogin"/>, done through the API as the
 * people who would do it: a passed vehicle on a published truck list, a crew, an insurance, a route with a hub, a UK
 * border and an overnight stop, a ferry, a registered handover Receiver and the declarations. Returns the convoy's id.
 */
export async function departedConvoyLedBy(leaderLogin: Login): Promise<number> {
  const admin = await tokenFor('admin');
  const dispatcher = await tokenFor('operator');
  const groundOfficer = await tokenFor('groundofficer');
  const stamp = `${String(Date.now())}${String(Math.floor(Math.random() * 1000))}`;
  const vin = `E2EC${stamp}`.slice(0, 17).padEnd(17, '0');

  await call('/vehicles', dispatcher, 'POST', {
    vin,
    plate: 'E2E 777',
    year: 2016,
    fuel: 'Diesel',
    transmission: 'Manual',
    weightKg: 2100,
  });
  await call(`/vehicles/${vin}/inspection`, dispatcher, 'PUT', { status: 'Passed' });

  const convoyId = idFrom(
    await call('/convoys', dispatcher, 'POST', {
      start: '2026-09-01T06:00:00Z',
      expectedEnd: '2026-09-05T18:00:00Z',
    }),
  );
  const convoy = `/convoys/${convoyId}`;
  await call(`${convoy}/vehicles/${vin}`, dispatcher, 'PUT');

  const leader = await volunteerOf(leaderLogin, admin);
  await call(`${convoy}/vehicles/${vin}/crew/${leader}`, dispatcher, 'PUT', {});
  const today = new Date().toISOString().slice(0, 10);
  const later = new Date(Date.now() + 30 * 86_400_000).toISOString().slice(0, 10);
  await call(`${convoy}/vehicles/${vin}/insurance`, dispatcher, 'PUT', {
    insurer: 'Ukraine Aid Mutual',
    policyNumber: 'E2E-1',
    coverStart: today,
    coverEnd: later,
  });
  await call(`${convoy}/publish-truck-list`, dispatcher, 'POST');

  const receiver = idFrom(
    await call('/receivers', groundOfficer, 'POST', {
      organisation: 'E2E Hospital',
      region: 'Kharkiv oblast',
    }),
  );
  await call(`/receivers/${receiver}/status`, admin, 'PUT', { status: 'Registered' });

  await call(`${convoy}/route`, admin, 'PUT', {
    stops: [
      {
        name: 'Coventry depot',
        kind: 'Hub',
        city: 'Coventry',
        country: 'United Kingdom',
        postcode: 'CV1 2AB',
      },
      {
        name: 'Calais border',
        kind: 'Border',
        authority: 'UK',
        city: 'Calais',
        country: 'France',
        postcode: '62100',
      },
      {
        name: 'Krakow stay',
        kind: 'Overnight',
        city: 'Krakow',
        country: 'Poland',
        postcode: '30-001',
      },
    ],
  });
  await call(`${convoy}/leader`, admin, 'PUT', { personId: leader });
  await call(`${convoy}/vehicles/${vin}/ferry`, admin, 'PUT', {
    operator: 'P&O Ferries',
    reference: 'E2E-FERRY',
    sailingAt: '2026-09-02T07:30:00Z',
    ticketDetails: 'Freight',
  });
  await call(`${convoy}/vehicles/${vin}/handover-receiver`, admin, 'PUT', {
    receiverRef: receiver,
  });
  await call(`${convoy}/vehicles/${vin}/declarations/ens`, admin, 'PUT', {
    mrn: `26FR${stamp.padEnd(14, '0').slice(0, 14)}`,
    acceptedAt: '2026-08-24T09:30:00+00:00',
    filedBy: 'groundofficer',
    filingReference: 'STP-E2E',
  });
  await call(`${convoy}/vehicles/${vin}/declarations/gmr/record`, admin, 'POST', {
    reference: 'GMR-E2E',
  });
  await call(`${convoy}/vehicles/${vin}/declarations/elo/record`, admin, 'POST', {
    reference: 'ELO-E2E',
  });
  await call(`${convoy}/depart`, dispatcher, 'POST');

  return Number(convoyId);
}
