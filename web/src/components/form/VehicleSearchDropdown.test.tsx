import { http } from 'msw';
import { userEvent } from 'vitest/browser';
import { expect, test, vi } from 'vitest';

import { makeVehicle } from '../../test/factories/vehicle';
import { problem } from '../../test/msw/problem';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { VehicleSearchDropdown } from './VehicleSearchDropdown';

function renderDropdown(excludeVins: readonly string[] = []) {
  const onSelect = vi.fn<(vin: string) => void>();
  const screen = renderWithProviders(
    <VehicleSearchDropdown onSelect={onSelect} excludeVins={excludeVins} />,
    { roles: ['Dispatcher'] },
  );
  return { screen, onSelect };
}

const passed = makeVehicle({
  vin: 'VIN-PASSED-01',
  plate: 'PA55 ED1',
  brand: 'Iveco',
  model: 'Daily',
  inspectionStatus: 'Passed',
});

test('offers only vehicles that have passed their inspection and are still here', async () => {
  worker.use(
    ...vehicleApi([
      passed,
      makeVehicle({ vin: 'VIN-PENDING-01', inspectionStatus: 'Pending' }),
      makeVehicle({ vin: 'VIN-INSPECTING', inspectionStatus: 'Inspecting' }),
      makeVehicle({ vin: 'VIN-FAILED-001', inspectionStatus: 'Failed', servicing: true }),
      makeVehicle({
        vin: 'VIN-GONE-00001',
        inspectionStatus: 'Passed',
        handedOverAt: '2026-06-05T17:00:00',
      }),
    ]).handlers,
  );
  const screen = await renderDropdown().screen;

  await screen.getByLabelText('Vehicle').click();

  await expect.element(screen.getByRole('option', { name: /VIN-PASSED-01/ })).toBeInTheDocument();
  expect(screen.getByRole('option').elements()).toHaveLength(1);
});

test.each([
  ['plate', 'pa55'],
  ['brand', 'iveco'],
  ['model', 'daily'],
  ['VIN', 'passed-01'],
])('finds a vehicle by its %s', async (_field, search) => {
  worker.use(
    ...vehicleApi([passed, makeVehicle({ vin: 'VIN-OTHER-0001', inspectionStatus: 'Passed' })])
      .handlers,
  );
  const screen = await renderDropdown().screen;

  await screen.getByLabelText('Vehicle').fill(search);

  await expect.element(screen.getByRole('option', { name: /VIN-PASSED-01/ })).toBeInTheDocument();
  expect(screen.getByRole('option').elements()).toHaveLength(1);
});

test('leaves out vehicles already on the convoy', async () => {
  worker.use(...vehicleApi([passed]).handlers);
  const screen = await renderDropdown(['VIN-PASSED-01']).screen;

  await screen.getByLabelText('Vehicle').click();

  await expect
    .element(screen.getByText('No vehicles have passed inspection yet'))
    .toBeInTheDocument();
});

test('says when the search matches nothing', async () => {
  worker.use(...vehicleApi([passed]).handlers);
  const screen = await renderDropdown().screen;

  await screen.getByLabelText('Vehicle').fill('no-such-thing');

  await expect.element(screen.getByText('No passed vehicle matches')).toBeInTheDocument();
});

test('says so when the fleet cannot be loaded', async () => {
  worker.use(http.get('/vehicles', () => problem(500, 'Database unavailable')));
  const screen = await renderDropdown().screen;

  await expect.element(screen.getByRole('alert')).toHaveTextContent('could not be loaded');
});

test('selects a vehicle with the mouse', async () => {
  worker.use(...vehicleApi([passed]).handlers);
  const { screen: rendered, onSelect } = renderDropdown();
  const screen = await rendered;

  await screen.getByLabelText('Vehicle').click();
  await screen.getByRole('option', { name: /VIN-PASSED-01/ }).click();

  expect(onSelect).toHaveBeenCalledWith('VIN-PASSED-01');
  await expect.element(screen.getByRole('option')).not.toBeInTheDocument();
});

test('selects a vehicle with the keyboard', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({ vin: 'VIN-A-00000001', inspectionStatus: 'Passed' }),
      makeVehicle({ vin: 'VIN-B-00000002', inspectionStatus: 'Passed' }),
    ]).handlers,
  );
  const { screen: rendered, onSelect } = renderDropdown();
  const screen = await rendered;
  const input = screen.getByLabelText('Vehicle');

  await input.click();
  await expect.element(screen.getByRole('option', { name: /VIN-A/ })).toBeInTheDocument();
  await userEvent.keyboard('{ArrowDown}{ArrowDown}');

  await expect
    .element(screen.getByRole('option', { name: /VIN-B/ }))
    .toHaveAttribute('aria-selected', 'true');
  await expect.element(input).toHaveAttribute('aria-expanded', 'true');

  await userEvent.keyboard('{Enter}');

  expect(onSelect).toHaveBeenCalledWith('VIN-B-00000002');
});

test('Escape closes the list without choosing', async () => {
  worker.use(...vehicleApi([passed]).handlers);
  const { screen: rendered, onSelect } = renderDropdown();
  const screen = await rendered;

  await screen.getByLabelText('Vehicle').click();
  await expect.element(screen.getByRole('option')).toBeInTheDocument();
  await userEvent.keyboard('{Escape}');

  await expect.element(screen.getByRole('option')).not.toBeInTheDocument();
  expect(onSelect).not.toHaveBeenCalled();
});
