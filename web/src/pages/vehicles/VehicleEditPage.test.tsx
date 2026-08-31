import type { RouteObject } from 'react-router-dom';
import { afterEach, beforeEach, expect, test } from 'vitest';

import { resetApiClient } from '../../api/client';
import { makeVehicle } from '../../test/factories/vehicle';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { vehicleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'vehicles', children: vehicleRoutes },
];

beforeEach(() => {
  resetApiClient();
});
afterEach(() => {
  resetApiClient();
});

test('pre-populates the form from the vehicle', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X', plate: 'ED11 TME' })]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X/edit',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByLabelText('Number plate')).toHaveValue('ED11 TME');
});

test('saves changes and returns to the detail page', async () => {
  const api = vehicleApi([makeVehicle({ vin: 'VIN-X', plate: 'OLD 111' })]);
  worker.use(...api.handlers);

  const screen = renderWithProviders(null, {
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

test('renders Not found when editing a VIN that does not exist', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/vehicles/GONE/edit',
    roles: ['Purchaser'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});
