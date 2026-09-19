import { http } from 'msw';
import { expect, test } from 'vitest';

import type { VehicleReadModel } from '../../api/schemas/vehicles';
import { makeConvoy } from '../../test/factories/convoy';
import { makeVehicle } from '../../test/factories/vehicle';
import { convoyApi } from '../../test/msw/convoys';
import { problem } from '../../test/msw/problem';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyVehiclesPanel } from './ConvoyVehiclesPanel';

function serve(vehicles: readonly VehicleReadModel[], { published = false } = {}) {
  const fleet = vehicleApi(vehicles);
  const convoys = convoyApi([makeConvoy({ id: 5, truckListPublished: published })], {
    fleet: fleet.db,
  });
  worker.use(...convoys.handlers, ...fleet.handlers);
  return convoys;
}

async function pick(screen: Awaited<ReturnType<typeof renderWithProviders>>, vin: string) {
  await screen.getByLabelText('Vehicle').fill(vin);
  await screen.getByRole('option', { name: new RegExp(vin) }).click();
}

test('assigns a vehicle that has passed its inspection and then removes it', async () => {
  const convoys = serve([makeVehicle({ vin: 'VIN-XYZ-9', inspectionStatus: 'Passed' })]);

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('No vehicles assigned yet.')).toBeInTheDocument();
  await pick(screen, 'VIN-XYZ-9');

  await expect.element(screen.getByRole('cell', { name: 'VIN-XYZ-9' })).toBeInTheDocument();
  expect(convoys.vehicles.get(5)?.map((v) => v.vin)).toEqual(['VIN-XYZ-9']);

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('No vehicles assigned yet.')).toBeInTheDocument();
  expect(convoys.vehicles.get(5)).toEqual([]);
});

test('surfaces a 409 detail when the truck list was published under the operator', async () => {
  serve([makeVehicle({ vin: 'VIN-XYZ-9', inspectionStatus: 'Passed' })], { published: true });

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await pick(screen, 'VIN-XYZ-9');

  await expect
    .element(screen.getByText('The truck list for this convoy has been published.'))
    .toBeInTheDocument();
});

test('a published truck list offers no way to change it', async () => {
  serve([makeVehicle({ vin: 'VIN-XYZ-9', inspectionStatus: 'Passed' })], { published: true });

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('status')).toHaveTextContent('truck list is published');
  await expect.element(screen.getByLabelText('Vehicle')).not.toBeInTheDocument();
});

test('says so when the convoy vehicles cannot be loaded', async () => {
  worker.use(
    http.get('/convoys/:id/vehicles', () => problem(500, 'Database unavailable')),
    ...vehicleApi([]).handlers,
  );

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('alert')).toHaveTextContent('could not be loaded');
});
