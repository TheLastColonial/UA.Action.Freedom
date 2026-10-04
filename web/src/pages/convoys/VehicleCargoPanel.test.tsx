import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { makeManifestBox } from '../../test/factories/manifest';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { VehicleCargoPanel } from './VehicleCargoPanel';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  convoys.vehicles.set(7, [
    makeConvoyVehicle({ vin: 'VIN-A', plate: 'PL-001' }),
    makeConvoyVehicle({ vin: 'VIN-B', plate: 'PL-002' }),
  ]);
  worker.use(...convoys.handlers);
  return convoys;
}

function renderPanel(vin = 'VIN-A', plate = 'PL-001', role: Role = 'Loader', withdrawn = false) {
  return renderWithProviders(
    <VehicleCargoPanel convoyId={7} vin={vin} plate={plate} withdrawn={withdrawn} />,
    { roles: [role] },
  );
}

test('says so when nothing is on the vehicle yet', async () => {
  serve();

  const screen = await renderPanel();

  await expect.element(screen.getByText('No boxes on this vehicle yet.')).toBeInTheDocument();
});

test('a loader puts a box on the vehicle and takes it off again', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Box id to add to PL-001').fill('9');
  await screen.getByRole('button', { name: 'Add box' }).click();
  await expect.element(screen.getByText('#9')).toBeInTheDocument();
  expect(convoys.boxes.get('7:VIN-A')).toHaveLength(1);

  await screen.getByRole('button', { name: 'Remove' }).click();
  await expect.element(screen.getByText('No boxes on this vehicle yet.')).toBeInTheDocument();
});

test('putting a box on a second vehicle moves it off the first', async () => {
  const convoys = serve();
  convoys.boxes.set('7:VIN-A', [makeManifestBox({ boxId: 3 })]);
  const screen = await renderPanel('VIN-B', 'PL-002');

  await screen.getByLabelText('Box id to add to PL-002').fill('3');
  await screen.getByRole('button', { name: 'Add box' }).click();

  await expect.element(screen.getByText('#3')).toBeInTheDocument();
  expect(convoys.boxes.get('7:VIN-A')).toHaveLength(0);
  expect(convoys.boxes.get('7:VIN-B')).toHaveLength(1);
});

test('a withdrawn vehicle takes no more boxes', async () => {
  const convoys = serve();
  convoys.boxes.set('7:VIN-A', [makeManifestBox({ boxId: 3 })]);

  const screen = await renderPanel('VIN-A', 'PL-001', 'Loader', true);

  await expect.element(screen.getByText('#3')).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add box' })).not.toBeInTheDocument();
});

test('a role that does not handle boxes sees the cargo but cannot change it', async () => {
  const convoys = serve();
  convoys.boxes.set('7:VIN-A', [makeManifestBox({ boxId: 3 })]);

  const screen = await renderPanel('VIN-A', 'PL-001', 'Mechanic');

  await expect.element(screen.getByText('#3')).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add box' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
});
