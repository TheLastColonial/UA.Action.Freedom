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

test('renders a stub page showing the current servicing status', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X', servicing: true })]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X/servicing',
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Servicing — VIN-X' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText('In for servicing: Yes')).toBeInTheDocument();
  await expect
    .element(screen.getByRole('link', { name: 'Back to vehicle' }))
    .toHaveAttribute('href', '/vehicles/VIN-X');
});

test('renders Not found for a VIN that does not exist', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = await renderWithProviders(null, {
    routes,
    route: '/vehicles/UNKNOWN/servicing',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});
