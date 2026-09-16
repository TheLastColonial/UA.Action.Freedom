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

test('renders cargo capacity when it has been recorded', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({
        vin: 'VIN-X',
        maxCargoWeightKg: 900.5,
        cargoWidthCm: 150.25,
        cargoDepthCm: 300,
        cargoHeightCm: 180.75,
      }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-X', roles: ['Loader'] });

  await expect.element(screen.getByText('900.5 kg')).toBeInTheDocument();
  await expect.element(screen.getByText('150.25 × 300 × 180.75 cm')).toBeInTheDocument();
});

test('renders a dash for cargo capacity that has never been measured', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-Y' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-Y', roles: ['Loader'] });

  await expect.element(screen.getByRole('heading', { name: 'VIN-Y' })).toBeInTheDocument();
  await expect.element(screen.getByText('Maximum cargo weight')).toBeInTheDocument();
  await expect.element(screen.getByText('Cargo dimensions (W × D × H)')).toBeInTheDocument();
});

test('groups fields into named cards', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-X', roles: ['Loader'] });

  for (const name of [
    'Vehicle details',
    'Engine details',
    'Cargo capacity',
    'Status',
    'Purchase information',
    'Notes',
  ]) {
    await expect.element(screen.getByRole('region', { name })).toBeInTheDocument();
  }
});

test('renders purchase information when recorded', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({ vin: 'VIN-X', purchaserName: 'A. Buyer', purchaseDate: '2026-01-15T00:00:00Z' }),
    ]).handlers,
  );

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-X', roles: ['Loader'] });

  await expect.element(screen.getByText('A. Buyer')).toBeInTheDocument();
  await expect.element(screen.getByText('2026-01-15')).toBeInTheDocument();
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

test('links to the servicing stub page', async () => {
  worker.use(...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers);

  const screen = renderWithProviders(null, { routes, route: '/vehicles/VIN-X', roles: ['Loader'] });

  await expect
    .element(screen.getByRole('link', { name: 'Servicing' }))
    .toHaveAttribute('href', '/vehicles/VIN-X/servicing');
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
