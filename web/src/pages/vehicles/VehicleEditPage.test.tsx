import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import { makeVehicle } from '../../test/factories/vehicle';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { vehicleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'vehicles', children: vehicleRoutes },
];

test('pre-populates the form from the vehicle', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X', plate: 'ED11 TME' })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X/edit',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByLabelText('Number plate')).toHaveValue('ED11 TME');
});

test('saves changes and returns to the detail page', async () => {
  const api = vehicleApi([makeVehicle({ vin: 'VIN-X', plate: 'OLD 111' })]);
  worker.use(...api.handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X/edit',
    roles: ['Purchaser'],
  });

  const plate = screen.getByLabelText('Number plate');
  await plate.fill('NEW 222');
  await screen.getByRole('button', { name: 'Save changes' }).click();

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByText('NEW 222')).toBeInTheDocument();
});

test('preserves an existing convoy assignment and servicing flag on unrelated edits', async () => {
  const vehicle = makeVehicle({ vin: 'VIN-X', convoyId: 7, servicing: true, colour: 'blue' });
  worker.use(...vehicleApi([vehicle]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X/edit',
    roles: ['Purchaser'],
  });

  await screen.getByLabelText('Colour').fill('green');
  await screen.getByRole('button', { name: 'Save changes' }).click();

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByText('green')).toBeInTheDocument();
  await expect.element(screen.getByText('7')).toBeInTheDocument();
  await expect.element(screen.getByText('Yes')).toBeInTheDocument();
});

test('pre-populates and can change the cargo capacity fields', async () => {
  const vehicle = makeVehicle({ vin: 'VIN-X', maxCargoWeightKg: 800, cargoWidthCm: 100 });
  worker.use(...vehicleApi([vehicle]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X/edit',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByLabelText('Maximum weight (kg)')).toHaveValue(800);
  await screen.getByLabelText('Maximum weight (kg)').fill('950.5');
  await screen.getByRole('button', { name: 'Save changes' }).click();

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByText('950.5 kg')).toBeInTheDocument();
});

test('renders Not found when editing a VIN that does not exist', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/GONE/edit',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});
