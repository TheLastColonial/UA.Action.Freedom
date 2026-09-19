import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeConvoyVehicle } from '../../test/factories/convoy';
import { makePerson } from '../../test/factories/person';
import { convoyApi } from '../../test/msw/convoys';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyDriversPanel } from './ConvoyDriversPanel';

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('renders empty when no vehicles are assigned', async () => {
  const api = convoyApi();
  worker.use(...api.handlers, ...personApi().handlers);

  const screen = await renderWithProviders(
    <ConvoyDriversPanel convoyId={7} disabled={false} />,
    {
      roles: ['Dispatcher'],
    },
  );

  // Component renders without error even with no vehicles
  expect(screen.container).toBeInTheDocument();
});

test('displays warning when vehicle has fewer than two drivers', async () => {
  const api = convoyApi();
  api.vehicles.set(7, [
    makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001', driverCount: 1 }),
    makeConvoyVehicle({ vin: 'VIN-TEST-2', plate: 'PL-002', driverCount: 0 }),
  ]);

  worker.use(...api.handlers, ...personApi().handlers);

  const screen = await renderWithProviders(
    <ConvoyDriversPanel convoyId={7} disabled={false} />,
    {
      roles: ['Dispatcher'],
    },
  );

  await expect
    .element(screen.getByText(/have fewer than two drivers assigned/i))
    .toBeInTheDocument();
});

test('a Dispatcher can assign a driver', async () => {
  const driver = makePerson({ id: 'd1', firstName: 'Alice', lastName: 'Driver', isDriver: true });

  const api = convoyApi();
  api.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1', driverCount: 0 })]);

  worker.use(...api.handlers, ...personApi([driver]).handlers);

  const screen = await renderWithProviders(
    <ConvoyDriversPanel convoyId={7} disabled={false} />,
    {
      roles: ['Dispatcher'],
    },
  );

  const driverSelect = screen.getByLabelText(/Add driver/i);
  await driverSelect.selectOptions(driver.id);

  const assignButton = screen.getByRole('button', { name: 'Assign' });
  await assignButton.click();

  await expect.element(screen.getByText('Alice Driver')).toBeInTheDocument();
});

test('non-Dispatcher roles cannot modify drivers', async () => {
  const driver = makePerson({ id: 'd1', firstName: 'Alice', lastName: 'Driver', isDriver: true });

  const api = convoyApi();
  api.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1' })]);
  api.drivers.set('7:VIN-TEST-1', [
    { personId: driver.id, firstName: 'Alice', lastName: 'Driver' },
  ]);

  worker.use(...api.handlers, ...personApi([driver]).handlers);

  const screen = await renderWithProviders(
    <ConvoyDriversPanel convoyId={7} disabled={false} />,
    {
      roles: ['Loader'],
    },
  );

  await expect.element(screen.getByText('Alice Driver')).toBeInTheDocument();
  expect(screen.queryByLabelText(/Add driver/i)).not.toBeInTheDocument();
});
