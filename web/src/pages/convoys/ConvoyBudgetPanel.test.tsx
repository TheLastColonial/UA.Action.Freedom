import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { budgetApi } from '../../test/msw/budget';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyBudgetPanel } from './ConvoyBudgetPanel';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' })]);
  const budget = budgetApi();
  worker.use(...convoys.handlers, ...budget.handlers);
  return budget;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<ConvoyBudgetPanel convoyId={7} />, { roles: [role] });
}

test('says when no budget has been set and that it can wait', async () => {
  serve();

  const screen = await renderPanel();

  await expect.element(screen.getByText(/No budget set/)).toBeInTheDocument();
});

test('a dispatcher allocates the budget and only the filled lines are stored', async () => {
  const budget = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Fuel budget (£)').fill('1000');
  await screen.getByLabelText('Ferry budget (£)').fill('600.50');
  await screen.getByRole('button', { name: 'Save budget' }).click();

  await expect.element(screen.getByText('£1000.00')).toBeInTheDocument();
  expect(budget.lines.get(7)?.map((line) => [line.type, line.plannedGbp])).toEqual([
    ['Fuel', 1000],
    ['Ferry', 600.5],
  ]);
});

test('a negative amount is refused before anything is sent', async () => {
  const budget = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Fuel budget (£)').fill('-5');
  await screen.getByRole('button', { name: 'Save budget' }).click();

  await expect.element(screen.getByText('Amount cannot be negative')).toBeInTheDocument();
  expect(budget.lines.has(7)).toBe(false);
});

test('fuel costs over the line show it as over budget', async () => {
  serve();
  const screen = await renderPanel();
  await screen.getByLabelText('Fuel budget (£)').fill('1000');
  await screen.getByRole('button', { name: 'Save budget' }).click();

  await screen.getByLabelText('Amount (£)').fill('1100');
  await screen.getByRole('button', { name: 'Add cost' }).click();

  await expect.element(screen.getByText('Over budget')).toBeInTheDocument();
  await expect.element(screen.getByText(/Fuel £1100.00/)).toBeInTheDocument();
});

test('a ferry cost on a booking appears as an actual without being entered', async () => {
  const budget = serve();
  budget.booked.set(7, [{ type: 'Ferry', amountGbp: 310 }]);

  const screen = await renderPanel();

  await expect.element(screen.getByRole('row', { name: /Ferry.*£310.00/ })).toBeInTheDocument();
});

test('a cost can be deleted', async () => {
  const budget = serve();
  const screen = await renderPanel();
  await screen.getByLabelText('Amount (£)').fill('50');
  await screen.getByRole('button', { name: 'Add cost' }).click();
  await expect.element(screen.getByText(/Fuel £50.00/)).toBeInTheDocument();

  await screen.getByRole('button', { name: 'Delete Fuel cost £50.00' }).click();

  await expect.element(screen.getByText('No costs entered yet.')).toBeInTheDocument();
  expect(budget.costs.get(7)).toEqual([]);
});

test('only fuel and other can be entered', async () => {
  serve();
  const screen = await renderPanel();

  await expect.element(screen.getByLabelText('Cost type')).toBeInTheDocument();
  const options = screen.getByLabelText('Cost type').element().querySelectorAll('option');

  expect([...options].map((option) => option.value)).toEqual(['Fuel', 'Other']);
});

test('a loader sees the budget but cannot change it', async () => {
  serve();

  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText(/No budget set/)).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Save budget' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Add cost' })).not.toBeInTheDocument();
});
