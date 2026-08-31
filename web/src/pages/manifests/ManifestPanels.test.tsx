import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeManifest, makeManifestBox } from '../../test/factories/manifest';
import { makePerson } from '../../test/factories/person';
import { manifestApi } from '../../test/msw/manifests';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ManifestBoxesPanel } from './ManifestBoxesPanel';
import { ManifestTeamsPanel } from './ManifestTeamsPanel';
import { ManifestWeightPanel } from './ManifestWeightPanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('teams panel assigns a lead driver for the UK leg', async () => {
  const api = manifestApi([makeManifest({ id: 'T1' })], { knownDriverIds: ['d1'] });
  worker.use(
    ...api.handlers,
    ...personApi([makePerson({ id: 'd1', firstName: 'Dana', lastName: 'Road', isDriver: true })])
      .handlers,
  );

  const screen = renderWithProviders(<ManifestTeamsPanel manifestId="T1" frozen={false} />, {
    roles: ['Dispatcher'],
  });

  const legForm = screen.getByRole('group', { name: 'UK → Europe' });
  await legForm.getByLabelText('Lead driver').selectOptions('d1');
  await legForm.getByRole('button', { name: 'Save UK → Europe team' }).click();

  await expect.poll(() => api.teams.get('T1')?.[0]?.primaryPersonId).toBe('d1');
});

test('teams panel rejects the same volunteer on both seats', async () => {
  const api = manifestApi([makeManifest({ id: 'T2' })]);
  worker.use(
    ...api.handlers,
    ...personApi([makePerson({ id: 'd1', firstName: 'Dana', lastName: 'Road', isDriver: true })])
      .handlers,
  );

  const screen = renderWithProviders(<ManifestTeamsPanel manifestId="T2" frozen={false} />, {
    roles: ['Dispatcher'],
  });

  const legForm = screen.getByRole('group', { name: 'UK → Europe' });
  await legForm.getByLabelText('Lead driver').selectOptions('d1');
  await legForm.getByLabelText('Second driver').selectOptions('d1');
  await legForm.getByRole('button', { name: 'Save UK → Europe team' }).click();

  await expect
    .element(
      screen.getByText('A driver team is two people — the same volunteer cannot crew both halves.'),
    )
    .toBeInTheDocument();
});

test('cargo panel adds and removes a box', async () => {
  worker.use(...manifestApi([makeManifest({ id: 'C1' })]).handlers);

  const screen = renderWithProviders(<ManifestBoxesPanel manifestId="C1" frozen={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('No boxes on this manifest yet.')).toBeInTheDocument();

  await screen.getByLabelText('Box id to add').fill('9');
  await screen.getByRole('button', { name: 'Add box' }).click();
  await expect.element(screen.getByText('#9')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('No boxes on this manifest yet.')).toBeInTheDocument();
});

test('cargo panel is read-only when the manifest is frozen', async () => {
  const api = manifestApi([makeManifest({ id: 'C2', frozen: true })]);
  api.boxes.set('C2', [makeManifestBox({ boxId: 3 })]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(<ManifestBoxesPanel manifestId="C2" frozen />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('#3')).toBeInTheDocument();
  await expect
    .element(screen.getByText('Frozen — cargo can no longer be changed.'))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add box' })).not.toBeInTheDocument();
});

test('weight panel shows the border-check total and a provisional warning', async () => {
  const api = manifestApi([makeManifest({ id: 'W1' })]);
  api.boxes.set('W1', [
    makeManifestBox({ boxId: 1, weightKg: 20, validated: true }),
    makeManifestBox({ boxId: 2, weightKg: 10, validated: false }),
  ]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(<ManifestWeightPanel manifestId="W1" />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('2275 kg')).toBeInTheDocument();
  await expect
    .element(screen.getByText('1 box(es) are not yet validated — this total is provisional.'))
    .toBeInTheDocument();
});
