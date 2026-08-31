import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoy } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyVehiclesPanel } from './ConvoyVehiclesPanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('assigns a vehicle and then removes it', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 5 })]).handlers);

  const screen = renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('No vehicles assigned yet.')).toBeInTheDocument();

  await screen.getByLabelText('VIN to assign').fill('VIN-XYZ-9');
  await screen.getByRole('button', { name: 'Assign vehicle' }).click();

  await expect.element(screen.getByText('VIN-XYZ-9')).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('No vehicles assigned yet.')).toBeInTheDocument();
});

test('surfaces a 409 detail when the truck list was published under the operator', async () => {
  worker.use(...convoyApi([makeConvoy({ id: 5, truckListPublished: true })]).handlers);

  const screen = renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('VIN to assign').fill('VIN-XYZ-9');
  await screen.getByRole('button', { name: 'Assign vehicle' }).click();

  await expect
    .element(screen.getByText('The truck list for this convoy has been published.'))
    .toBeInTheDocument();
});
