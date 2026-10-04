import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { budgetApi } from '../../test/msw/budget';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyEquipmentPanel } from './ConvoyEquipmentPanel';

function serve(withVehicle = true) {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  if (withVehicle) {
    convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' })]);
  }
  const budget = budgetApi();
  budget.catalogue.push({ id: 1, name: 'Warning triangle', unitCostGbp: 6.5 });
  worker.use(...convoys.handlers, ...budget.handlers);
  return budget;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<ConvoyEquipmentPanel convoyId={7} />, { roles: [role] });
}

test('asks for vehicles first when the truck list is empty', async () => {
  serve(false);

  const screen = await renderPanel();

  await expect
    .element(screen.getByText('Put vehicles on the truck list before adding equipment.'))
    .toBeInTheDocument();
});

test('a dispatcher adds equipment to a vehicle and its counted cost is shown', async () => {
  const budget = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Equipment item for PL-001').selectOptions('Warning triangle');
  await screen.getByLabelText('Quantity for PL-001').fill('2');
  await screen.getByRole('button', { name: 'Add equipment' }).click();

  await expect.element(screen.getByText(/2 × Warning triangle \(£13.00\)/)).toBeInTheDocument();
  expect(budget.equipment.get('7:VIN-TEST-1')).toMatchObject([
    { equipmentItemId: 1, quantity: 2, countedCostGbp: 13 },
  ]);
});

test('a line can be taken off a vehicle again', async () => {
  const budget = serve();
  const screen = await renderPanel();
  await screen.getByLabelText('Equipment item for PL-001').selectOptions('Warning triangle');
  await screen.getByRole('button', { name: 'Add equipment' }).click();
  await expect.element(screen.getByText(/1 × Warning triangle/)).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Remove Warning triangle from PL-001' }).click();

  await expect.element(screen.getByText('No equipment added.')).toBeInTheDocument();
  expect(budget.equipment.get('7:VIN-TEST-1')).toEqual([]);
});

test('an item must be chosen before anything is sent', async () => {
  const budget = serve();
  const screen = await renderPanel();

  await screen.getByRole('button', { name: 'Add equipment' }).click();

  await expect.element(screen.getByText('Choose an item')).toBeInTheDocument();
  expect(budget.equipment.size).toBe(0);
});

test('a new item joins the catalogue and a duplicate is refused', async () => {
  const budget = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Equipment name').fill('Tow strap');
  await screen.getByLabelText('Unit cost (£)').fill('12');
  await screen.getByRole('button', { name: 'Add to catalogue' }).click();
  await expect.poll(() => budget.catalogue.length).toBe(2);

  await screen.getByLabelText('Equipment name').fill('tow strap');
  await screen.getByRole('button', { name: 'Add to catalogue' }).click();
  await expect
    .element(screen.getByText('That item is already in the equipment catalogue.'))
    .toBeInTheDocument();
});

test('a loader can read the equipment but not change it', async () => {
  serve();

  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText('No equipment added.')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Add equipment' }))
    .not.toBeInTheDocument();
});
