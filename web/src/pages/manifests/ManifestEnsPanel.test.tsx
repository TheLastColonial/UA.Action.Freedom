import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeManifest } from '../../test/factories/manifest';
import { manifestApi } from '../../test/msw/manifests';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ManifestEnsPanel } from './ManifestEnsPanel';

function serve() {
  const api = manifestApi([makeManifest({ id: 'M1' })]);
  worker.use(...api.handlers);
  return api;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<ManifestEnsPanel manifestId="M1" />, { roles: [role] });
}

test('says when no declaration has been recorded', async () => {
  serve();

  const screen = await renderPanel();

  await expect
    .element(screen.getByText(/No ICS2 Entry Summary Declaration recorded/))
    .toBeInTheDocument();
});

test('a dispatcher records a declaration and it is stored for the manifest', async () => {
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
  expect(api.ens.get('M1')).toMatchObject({
    mrn: '26FR17551780961AT5',
    acceptedAt: '2026-08-24T09:30:00+00:00',
    filedBy: 'groundofficer',
    filingReference: 'STP-1',
  });
});

test('a malformed MRN is refused before anything is sent', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('MRN').fill('not-an-mrn');
  await screen.getByLabelText('Accepted at').fill('2026-08-24T09:30');
  await screen.getByLabelText('Filed by').fill('groundofficer');
  await screen.getByRole('button', { name: 'Record declaration' }).click();

  await expect.element(screen.getByText(/eighteen characters/)).toBeInTheDocument();
  expect(api.ens.has('M1')).toBe(false);
});

test('a recorded declaration can be withdrawn, and the form returns', async () => {
  const api = serve();
  api.ens.set('M1', {
    manifestId: 'M1',
    mrn: '26FR17551780961AT5',
    acceptedAt: '2026-08-24T09:30:00+00:00',
    filedBy: 'groundofficer',
    filingReference: null,
  });

  const screen = await renderPanel();

  await expect
    .element(screen.getByText('Declared under MRN 26FR17551780961AT5, filed by groundofficer.'))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Record declaration' }))
    .not.toBeInTheDocument();

  await screen.getByRole('button', { name: 'Withdraw declaration' }).click();

  await expect
    .element(screen.getByText(/No ICS2 Entry Summary Declaration recorded/))
    .toBeInTheDocument();
  expect(api.ens.has('M1')).toBe(false);
});

test('a role that cannot declare sees status but no form or withdraw button', async () => {
  const api = serve();
  api.ens.set('M1', {
    manifestId: 'M1',
    mrn: '26FR17551780961AT5',
    acceptedAt: '2026-08-24T09:30:00+00:00',
    filedBy: 'groundofficer',
    filingReference: null,
  });

  const screen = await renderPanel('Loader');

  await expect
    .element(screen.getByText('Declared under MRN 26FR17551780961AT5, filed by groundofficer.'))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Withdraw declaration' }))
    .not.toBeInTheDocument();
});
