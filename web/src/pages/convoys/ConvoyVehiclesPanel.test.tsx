import { http } from 'msw';
import { expect, test, vi } from 'vitest';

import type { VehicleReadModel } from '../../api/schemas/vehicles';
import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { makeReceiver } from '../../test/factories/receiver';
import { makeVehicle } from '../../test/factories/vehicle';
import { convoyApi } from '../../test/msw/convoys';
import { problem } from '../../test/msw/problem';
import { receiverApi } from '../../test/msw/receivers';
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

test('a dispatcher chooses a registered receiver as the vehicle handover receiver', async () => {
  const receivers = receiverApi([
    makeReceiver({ ref: 'r-reg', organisation: 'Kyiv Aid', region: 'Kyiv Oblast' }),
    makeReceiver({ ref: 'r-pending', organisation: 'Lviv Clinic', status: 'Pending' }),
  ]);
  const convoys = convoyApi([makeConvoy({ id: 5 })], {
    receiverStatuses: new Map([
      ['r-reg', 'Registered'],
      ['r-pending', 'Pending'],
    ]),
  });
  convoys.vehicles.set(5, [makeConvoyVehicle({ vin: 'VIN-1' })]);
  worker.use(...convoys.handlers, ...receivers.handlers);

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  const picker = screen.getByLabelText('Handover receiver for VIN-1');
  await expect.element(picker).toBeInTheDocument();
  await expect.element(screen.getByRole('option', { name: /Lviv Clinic/ })).not.toBeInTheDocument();

  await picker.selectOptions('r-reg');

  await vi.waitFor(() => {
    expect(convoys.vehicles.get(5)?.[0]?.handoverReceiverRef).toBe('r-reg');
  });
});

test('a reader sees the handover receiver but cannot change it', async () => {
  const receivers = receiverApi([
    makeReceiver({ ref: 'r-reg', organisation: 'Kyiv Aid', region: 'Kyiv Oblast' }),
  ]);
  const convoys = convoyApi([makeConvoy({ id: 5 })]);
  convoys.vehicles.set(5, [makeConvoyVehicle({ vin: 'VIN-1', handoverReceiverRef: 'r-reg' })]);
  worker.use(...convoys.handlers, ...receivers.handlers);

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('Kyiv Aid — Kyiv Oblast')).toBeInTheDocument();
  await expect
    .element(screen.getByLabelText('Handover receiver for VIN-1'))
    .not.toBeInTheDocument();
});

test('shows why the API refused a receiver as the handover receiver', async () => {
  const receivers = receiverApi([makeReceiver({ ref: 'r-reg', organisation: 'Kyiv Aid' })]);
  // The picker offered it, then it was suspended: the API answer is shown.
  const convoys = convoyApi([makeConvoy({ id: 5 })], {
    receiverStatuses: new Map([['r-reg', 'Suspended']]),
  });
  convoys.vehicles.set(5, [makeConvoyVehicle({ vin: 'VIN-1' })]);
  worker.use(...convoys.handlers, ...receivers.handlers);

  const screen = await renderWithProviders(<ConvoyVehiclesPanel convoyId={5} disabled={false} />, {
    roles: ['Dispatcher'],
  });

  await screen.getByLabelText('Handover receiver for VIN-1').selectOptions('r-reg');

  await expect
    .element(screen.getByText('Only a registered receiver can be a destination.'))
    .toBeInTheDocument();
});
