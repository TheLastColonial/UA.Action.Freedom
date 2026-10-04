import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { VehicleFerryPanel } from './VehicleFerryPanel';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' })]);
  worker.use(...convoys.handlers);
  return convoys;
}

function renderPanel(role: Role = 'Dispatcher', withdrawn = false) {
  return renderWithProviders(
    <VehicleFerryPanel convoyId={7} vin="VIN-TEST-1" plate="PL-001" withdrawn={withdrawn} />,
    { roles: [role] },
  );
}

test('says when no ferry has been booked', async () => {
  serve();

  const screen = await renderPanel();

  await expect.element(screen.getByText('Ferry not booked')).toBeInTheDocument();
});

test('a dispatcher books the ferry and it is stored for the vehicle', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Ferry operator').fill('P&O Ferries');
  await screen.getByLabelText('Booking reference').fill('POF-48213');
  await screen.getByLabelText('Sailing').fill('2026-09-02T07:30');
  await screen.getByLabelText('Ticket details').fill('Freight, 2 occupants');
  await screen.getByLabelText('Cost (£)').fill('310.00');
  await screen.getByRole('button', { name: 'Book ferry' }).click();

  await expect.element(screen.getByText(/Booked with P&O Ferries/)).toBeInTheDocument();
  expect(convoys.ferry.get('7:VIN-TEST-1')).toMatchObject({
    operator: 'P&O Ferries',
    reference: 'POF-48213',
    ticketDetails: 'Freight, 2 occupants',
    costGbp: 310,
  });
});

test('a booking needs an operator and a reference before anything is sent', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByRole('button', { name: 'Book ferry' }).click();

  await expect.element(screen.getByText('Operator is required')).toBeInTheDocument();
  await expect.element(screen.getByText('Booking reference is required')).toBeInTheDocument();
  expect(convoys.ferry.has('7:VIN-TEST-1')).toBe(false);
});

test('cancelling the booking clears it', async () => {
  const convoys = serve();
  convoys.ferry.set('7:VIN-TEST-1', {
    convoyId: 7,
    vin: 'VIN-TEST-1',
    operator: 'DFDS',
    reference: 'DF-1',
    sailingAt: '2026-09-02T07:30:00',
    ticketDetails: null,
    costGbp: null,
    lastChangedByName: null,
    lastChangedAt: null,
  });
  const screen = await renderPanel();

  await expect.element(screen.getByText(/Booked with DFDS/)).toBeInTheDocument();
  await screen.getByRole('button', { name: 'Cancel booking' }).click();

  await expect.element(screen.getByText('Ferry not booked')).toBeInTheDocument();
});

test('a loader sees the booking but cannot change it', async () => {
  const convoys = serve();
  convoys.ferry.set('7:VIN-TEST-1', {
    convoyId: 7,
    vin: 'VIN-TEST-1',
    operator: 'DFDS',
    reference: 'DF-1',
    sailingAt: '2026-09-02T07:30:00',
    ticketDetails: null,
    costGbp: null,
    lastChangedByName: null,
    lastChangedAt: null,
  });

  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText(/Booked with DFDS/)).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Book ferry' })).not.toBeInTheDocument();
});
