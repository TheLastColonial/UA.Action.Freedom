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

test('renders the vehicle from the detail endpoint', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X', plate: 'ZZ99 ZZZ' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-X', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByText('ZZ99 ZZZ')).toBeInTheDocument();
});

test('renders Not found for a VIN that does not exist', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/vehicles/UNKNOWN',
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('hides Edit and Delete from a read-only role', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-X', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'VIN-X' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Delete' })).not.toBeInTheDocument();
});

test('deletes the vehicle and returns to the list', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = renderWithProviders(null, {
    routes,
    route: '/vehicles/VIN-X',
    roles: ['Purchaser'],
  });

  await screen.getByRole('button', { name: 'Delete' }).click();

  await expect.element(screen.getByRole('heading', { name: 'Vehicles' })).toBeInTheDocument();
  await expect.element(screen.getByText('No vehicles recorded yet.')).toBeInTheDocument();
});
