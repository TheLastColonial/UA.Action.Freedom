import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { declarationApi } from '../../test/msw/declarations';
import type { DeclarationApiOptions } from '../../test/msw/declarations';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { DeclarationsPanel } from './DeclarationsPanel';

const VIN = 'VIN-1';

function serve(options: DeclarationApiOptions = {}) {
  const api = declarationApi(options);
  worker.use(...api.handlers);
  return api;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<DeclarationsPanel convoyId={7} vin={VIN} />, { roles: [role] });
}

test('says when nothing has been filed', async () => {
  serve();

  const screen = await renderPanel();

  await expect.element(screen.getByText('GMR: not filed.')).toBeInTheDocument();
  await expect.element(screen.getByText('ELO: not filed.')).toBeInTheDocument();
  await expect
    .element(screen.getByText(/No ICS2 Entry Summary Declaration recorded/))
    .toBeInTheDocument();
  await expect.element(screen.getByText('No goods list recorded.')).toBeInTheDocument();
});

test('a dispatcher records the GMR reference and it shows as filed', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('GMR reference').fill('GMR-1');
  await screen.getByRole('button', { name: 'Record GMR reference' }).click();

  await expect.element(screen.getByText('GMR: Filed — reference GMR-1.')).toBeInTheDocument();
  expect(api.rows).toMatchObject([{ kind: 'Gmr', status: 'Filed', reference: 'GMR-1' }]);
});

test('a filed declaration can be marked refused with a bounded reason', async () => {
  const api = serve();
  const screen = await renderPanel();
  await screen.getByLabelText('GMR reference').fill('GMR-1');
  await screen.getByRole('button', { name: 'Record GMR reference' }).click();

  await screen.getByRole('button', { name: 'Mark GMR refused' }).click();

  await expect
    .element(screen.getByText('GMR: Refused — reference GMR-1 (data-error).'))
    .toBeInTheDocument();
  expect(api.rows[0]).toMatchObject({ status: 'Refused', reasonCode: 'data-error' });
});

test('in manual mode the file button says to record the reference instead', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByRole('button', { name: 'File GMR automatically' }).click();

  await expect.element(screen.getByText(/submission mode is manual/)).toBeInTheDocument();
  expect(api.rows).toHaveLength(0);
});

test('in automatic mode filing the GMR marks it filed', async () => {
  const api = serve({ automatic: true });
  const screen = await renderPanel();

  await screen.getByRole('button', { name: 'File GMR automatically' }).click();

  await expect.element(screen.getByText('GMR: Filed.')).toBeInTheDocument();
  expect(api.rows).toMatchObject([{ kind: 'Gmr', status: 'Filed', reference: null }]);
});

test('the ELO is refused until an ENS is recorded', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('ELO reference').fill('ELO-1');
  await screen.getByRole('button', { name: 'Record ELO reference' }).click();

  await expect.element(screen.getByText(/needs an accepted ENS/)).toBeInTheDocument();
  expect(api.rows).toHaveLength(0);
});

test('a dispatcher records an ENS and it is stored for the vehicle', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('MRN').fill('26fr17551780961at5');
  await screen.getByLabelText('Accepted at').fill('2026-08-24T09:30');
  await screen.getByLabelText('Filed by').fill('groundofficer');
  await screen.getByLabelText('Filing reference').fill('STP-1');
  await screen.getByRole('button', { name: 'Record declaration' }).click();

  await expect
    .element(screen.getByText('Declared under MRN 26FR17551780961AT5, filed by groundofficer.'))
    .toBeInTheDocument();
  expect([...api.ens.values()]).toMatchObject([
    {
      mrn: '26FR17551780961AT5',
      acceptedAt: '2026-08-24T09:30:00+00:00',
      filedBy: 'groundofficer',
      filingReference: 'STP-1',
    },
  ]);
});

test('a malformed MRN is refused before anything is sent', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('MRN').fill('not-an-mrn');
  await screen.getByLabelText('Accepted at').fill('2026-08-24T09:30');
  await screen.getByLabelText('Filed by').fill('groundofficer');
  await screen.getByRole('button', { name: 'Record declaration' }).click();

  await expect.element(screen.getByText(/eighteen characters/)).toBeInTheDocument();
  expect(api.rows).toHaveLength(0);
});

test('a recorded ENS can be withdrawn, and the form returns', async () => {
  const api = serve();
  const screen = await renderPanel();
  await screen.getByLabelText('MRN').fill('26FR17551780961AT5');
  await screen.getByLabelText('Accepted at').fill('2026-08-24T09:30');
  await screen.getByLabelText('Filed by').fill('groundofficer');
  await screen.getByRole('button', { name: 'Record declaration' }).click();
  await expect.element(screen.getByText(/Declared under MRN/)).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Withdraw declaration' }).click();

  await expect
    .element(screen.getByRole('button', { name: 'Record declaration' }))
    .toBeInTheDocument();
  expect(api.rows[0]).toMatchObject({
    kind: 'Ens',
    status: 'Withdrawn',
    reference: '26FR17551780961AT5',
  });
});

test('a goods list is recorded per receiver', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Receiver').fill('receiver-1');
  await screen.getByLabelText('Goods list reference').first().fill('UA-77');
  await screen.getByRole('button', { name: 'Record goods list' }).click();

  await expect
    .element(screen.getByText('Goods list for receiver-1: Filed — reference UA-77.'))
    .toBeInTheDocument();
  expect(api.rows).toMatchObject([
    { kind: 'GoodsList', receiverRef: 'receiver-1', reference: 'UA-77' },
  ]);
});

test('a loader may read the declarations but is offered no way to change them', async () => {
  serve();

  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText('GMR: not filed.')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Record GMR reference' }))
    .not.toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Record declaration' }))
    .not.toBeInTheDocument();
});

test('a stale declaration is flagged and withdrawing it starts a fresh draft to record again', async () => {
  const api = serve();
  api.rows.push({
    id: 50,
    convoyId: 7,
    vin: VIN,
    kind: 'Gmr',
    status: 'Stale',
    receiverRef: null,
    reference: 'GMR-1',
    reasonCode: null,
    recordedByName: null,
    recordedAt: null,
    lastChangedByName: null,
    lastChangedAt: null,
  });
  const screen = await renderPanel();

  await expect
    .element(screen.getByText(/the load has changed since GMR was filed/))
    .toBeInTheDocument();
  await screen.getByRole('button', { name: 'Withdraw GMR and re-declare' }).click();

  await expect.element(screen.getByText('GMR: Draft.')).toBeInTheDocument();
  expect(api.rows.map((row) => row.status)).toEqual(['Withdrawn', 'Draft']);
  expect(api.rows.map((row) => row.reference)).toEqual(['GMR-1', null]);
});

test('a Loader cannot withdraw a stale declaration', async () => {
  const api = serve();
  api.rows.push({
    id: 50,
    convoyId: 7,
    vin: VIN,
    kind: 'Gmr',
    status: 'Stale',
    receiverRef: null,
    reference: 'GMR-1',
    reasonCode: null,
    recordedByName: null,
    recordedAt: null,
    lastChangedByName: null,
    lastChangedAt: null,
  });

  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText('GMR: Stale — reference GMR-1.')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Withdraw GMR and re-declare' }))
    .not.toBeInTheDocument();
});
