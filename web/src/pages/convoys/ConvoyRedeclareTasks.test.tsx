import { expect, test } from 'vitest';

import { declarationApi } from '../../test/msw/declarations';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyRedeclareTasks } from './ConvoyRedeclareTasks';

test('says so when every declaration matches its load', async () => {
  worker.use(...declarationApi().handlers);

  const screen = await renderWithProviders(<ConvoyRedeclareTasks convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('Every declaration matches its load.')).toBeInTheDocument();
});

test('lists a task for each stale declaration with the resolution that fits', async () => {
  const api = declarationApi();
  worker.use(...api.handlers);
  for (const [id, vin, kind] of [
    [1, 'VIN-1', 'Gmr'],
    [2, 'VIN-2', 'Ens'],
  ] as const) {
    api.rows.push({
      id,
      convoyId: 7,
      vin,
      kind,
      status: 'Stale',
      receiverRef: null,
      reference: 'REF',
      reasonCode: null,
      recordedByName: null,
      recordedAt: null,
      lastChangedByName: null,
      lastChangedAt: null,
    });
  }

  const screen = await renderWithProviders(<ConvoyRedeclareTasks convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('GMR for VIN-1 is stale.')).toBeInTheDocument();
  await expect
    .element(screen.getByText(/Update it, or delete and recreate it/))
    .toBeInTheDocument();
  await expect.element(screen.getByText('ENS for VIN-2 is stale.')).toBeInTheDocument();
  await expect.element(screen.getByText(/The old MRN is kept as history/)).toBeInTheDocument();
});
