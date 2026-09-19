import { http } from 'msw';
import type { RouteObject } from 'react-router-dom';
import { expect, test } from 'vitest';

import type { InspectionStatus } from '../../api/schemas/vehicles';
import { makeVehicle } from '../../test/factories/vehicle';
import { problem } from '../../test/msw/problem';
import { vehicleApi } from '../../test/msw/vehicles';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import type { RenderOptions } from '../../test/render';
import { vehicleRoutes } from './routes';

const routes: RouteObject[] = [
  { path: '/', element: <div>home</div> },
  { path: 'vehicles', children: vehicleRoutes },
];

function renderServicing(vin: string, options: RenderOptions = {}) {
  return renderWithProviders(null, {
    routes,
    route: `/vehicles/${vin}/servicing`,
    roles: ['Mechanic'],
    ...options,
  });
}

test.each<[InspectionStatus, InspectionStatus, string]>([
  ['Pending', 'Inspecting', 'Currently being serviced'],
  ['Inspecting', 'Passed', 'Ready for convoy'],
  ['Inspecting', 'Failed', 'Issues found'],
])('a mechanic moves an inspection from %s to %s and it is saved', async (from, to, label) => {
  const api = vehicleApi([makeVehicle({ vin: 'VIN-X', inspectionStatus: from })]);
  worker.use(...api.handlers);

  const screen = await renderServicing('VIN-X');

  await screen.getByLabelText('Inspection status').selectOptions(to);
  await screen.getByLabelText(/Inspection notes/).fill(`Moved to ${to}`);
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await expect.element(screen.getByRole('status')).toHaveTextContent('Inspection saved');
  await expect.element(screen.getByText(label, { exact: true })).toBeInTheDocument();
  expect(api.db.get('VIN-X')).toMatchObject({
    inspectionStatus: to,
    inspectionNotes: `Moved to ${to}`,
  });
});

test('a saved inspection is still there when the page is opened again', async () => {
  const api = vehicleApi([makeVehicle({ vin: 'VIN-X', inspectionStatus: 'Pending' })]);
  worker.use(...api.handlers);

  const first = await renderServicing('VIN-X');
  await first.getByLabelText('Inspection status').selectOptions('Passed');
  await first.getByLabelText(/Inspection notes/).fill('New tyres fitted');
  await first.getByRole('button', { name: 'Save inspection' }).click();
  await expect.element(first.getByRole('status')).toBeInTheDocument();

  const reopened = await renderServicing('VIN-X');

  await expect.element(reopened.getByLabelText('Inspection status')).toHaveValue('Passed');
  await expect.element(reopened.getByLabelText(/Inspection notes/)).toHaveValue('New tyres fitted');
});

test('saving an inspection does not send or change any other vehicle field', async () => {
  const vehicle = makeVehicle({ vin: 'VIN-X', plate: 'KEEP ME', servicing: true });
  const api = vehicleApi([vehicle]);
  worker.use(...api.handlers);

  const screen = await renderServicing('VIN-X');
  await screen.getByLabelText('Inspection status').selectOptions('Passed');
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await expect.element(screen.getByRole('status')).toBeInTheDocument();
  expect(api.db.get('VIN-X')).toEqual({
    ...vehicle,
    inspectionStatus: 'Passed',
    inspectionNotes: null,
  });
});

test('clearing the notes saves no notes', async () => {
  const api = vehicleApi([
    makeVehicle({ vin: 'VIN-X', inspectionStatus: 'Failed', inspectionNotes: 'Brakes' }),
  ]);
  worker.use(...api.handlers);

  const screen = await renderServicing('VIN-X');
  await screen.getByLabelText(/Inspection notes/).clear();
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await expect.element(screen.getByRole('status')).toBeInTheDocument();
  expect(api.db.get('VIN-X')?.inspectionNotes).toBeNull();
});

test('notes longer than 2000 characters are refused before anything is sent', async () => {
  const api = vehicleApi([makeVehicle({ vin: 'VIN-X', inspectionStatus: 'Pending' })]);
  worker.use(...api.handlers);

  const screen = await renderServicing('VIN-X');
  await screen.getByLabelText(/Inspection notes/).fill('x'.repeat(2001));
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await expect
    .element(screen.getByText('Notes must not exceed 2000 characters'))
    .toBeInTheDocument();
  expect(api.db.get('VIN-X')?.inspectionStatus).toBe('Pending');
  expect(api.db.get('VIN-X')?.inspectionNotes).toBeNull();
});

test('a failed save is reported and nothing is marked as saved', async () => {
  worker.use(
    http.put('/vehicles/:vin/inspection', () => problem(500, 'Database unavailable')),
    ...vehicleApi([makeVehicle({ vin: 'VIN-X' })]).handlers,
  );

  const screen = await renderServicing('VIN-X');
  await screen.getByLabelText('Inspection status').selectOptions('Passed');
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await expect.element(screen.getByRole('alert')).toHaveTextContent('Database unavailable');
  await expect.element(screen.getByRole('status')).not.toBeInTheDocument();
});

test('the save button is disabled while the inspection is being saved', async () => {
  let release = (): void => undefined;
  const released = new Promise<void>((resolve) => {
    release = resolve;
  });
  const api = vehicleApi([makeVehicle({ vin: 'VIN-X' })]);
  worker.use(
    http.put('/vehicles/:vin/inspection', async () => {
      await released;
      return undefined;
    }),
    ...api.handlers,
  );

  const screen = await renderServicing('VIN-X');
  await screen.getByRole('button', { name: 'Save inspection' }).click();

  await expect.element(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled();
  release();
  await expect.element(screen.getByRole('status')).toBeInTheDocument();
});

test('a role that cannot record inspections sees the result but no form', async () => {
  worker.use(
    ...vehicleApi([
      makeVehicle({ vin: 'VIN-X', inspectionStatus: 'Failed', inspectionNotes: 'Clutch' }),
    ]).handlers,
  );

  const screen = await renderServicing('VIN-X', { roles: ['Loader'] });

  await expect.element(screen.getByText('Issues found', { exact: true })).toBeInTheDocument();
  await expect.element(screen.getByText('Clutch', { exact: true })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Save inspection' }))
    .not.toBeInTheDocument();
});

test('renders Not found for a VIN that does not exist', async () => {
  worker.use(...vehicleApi([]).handlers);

  const screen = await renderServicing('UNKNOWN');

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});
