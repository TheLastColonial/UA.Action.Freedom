import { expect, test } from 'vitest';

import type { FerryBookingReadModel } from '../../api/schemas/convoys';
import {
  makeConvoy,
  makeConvoyVehicle,
  makeInsurance,
  makeRouteStop,
  makeWithdrawnConvoyVehicle,
} from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyReadinessPanel } from './ConvoyReadinessPanel';

const HOSPITAL = 'bbbbbbbb-0000-4000-8000-000000000001';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7, start: '2026-09-01T06:00:00' })], {
    receiverStatuses: new Map([[HOSPITAL, 'Registered']]),
  });
  worker.use(...convoys.handlers);
  return convoys;
}

const insured = (vin: string) =>
  makeInsurance({
    convoyId: 7,
    vin,
    coverStart: '2026-08-25T00:00:00',
    coverEnd: '2026-09-30T00:00:00',
  });

const booked = (vin: string): FerryBookingReadModel => ({
  convoyId: 7,
  vin,
  operator: 'P&O Ferries',
  reference: 'POF-1',
  sailingAt: '2026-09-02T07:30:00',
  ticketDetails: 'Freight',
  costGbp: null,
  lastChangedByName: null,
  lastChangedAt: null,
});

/** A convoy that meets every requirement the fake models: route, leader, and a crewed, insured, booked vehicle. */
function makeReady(convoys: ReturnType<typeof serve>) {
  convoys.routes.set(7, [makeRouteStop()]);
  convoys.leaders.set(7, [
    {
      id: 1,
      convoyId: 7,
      personId: 'p-1',
      personName: 'Olena Bondar',
      from: '2026-08-30T00:00:00',
      until: null,
      lastChangedByName: null,
      lastChangedAt: null,
    },
  ]);
  convoys.vehicles.set(7, [
    makeConvoyVehicle({
      vin: 'VIN-1',
      plate: 'PL-001',
      driverCount: 2,
      handoverReceiverRef: HOSPITAL,
    }),
  ]);
  convoys.insurance.set('7:VIN-1', insured('VIN-1'));
  convoys.ferry.set('7:VIN-1', booked('VIN-1'));
}

test('a convoy that meets every requirement is ready', async () => {
  makeReady(serve());

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
});

test('groups what blocks departure by convoy and by vehicle, each with a link to where it is fixed', async () => {
  const convoys = serve();
  convoys.vehicles.set(7, [
    // Passengers do not count as drivers.
    makeConvoyVehicle({ vin: 'VIN-1', plate: 'PL-001', driverCount: 0, passengerCount: 2 }),
    makeConvoyVehicle({ vin: 'VIN-2', plate: 'PL-002', driverCount: 2 }),
  ]);
  convoys.insurance.set('7:VIN-2', {
    ...insured('VIN-2'),
    voided: true,
    voidedAt: '2026-08-30T00:00:00',
  });

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not ready yet' })).toBeInTheDocument();
  const convoyList = screen.getByRole('list', { name: 'Convoy requirements' });
  await expect.element(convoyList.getByText(/No route planned/)).toBeInTheDocument();
  await expect.element(convoyList.getByText(/No Convoy Leader assigned/)).toBeInTheDocument();
  const first = screen.getByRole('list', { name: 'PL-001 requirements' });
  await expect.element(first.getByText(/No driver assigned/)).toBeInTheDocument();
  await expect.element(first.getByText(/Insurance not recorded/)).toBeInTheDocument();
  const second = screen.getByRole('list', { name: 'PL-002 requirements' });
  await expect.element(second.getByText(/Insurance voided/)).toBeInTheDocument();
  await expect
    .element(convoyList.getByRole('link', { name: 'Dispatcher: fix' }).first())
    .toHaveAttribute('href', '/convoys/7?tab=route');
});

test('a withdrawn vehicle is not reported as unready', async () => {
  // It broke down and left: it has no crew to find and no insurance to renew, so holding the
  // convoy open for it would be reporting a problem nobody can fix.
  const convoys = serve();
  makeReady(convoys);
  convoys.vehicles.set(7, [
    ...(convoys.vehicles.get(7) ?? []),
    makeWithdrawnConvoyVehicle({ vin: 'VIN-2', plate: 'PL-002' }),
  ]);

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText(/PL-002/)).not.toBeInTheDocument();
});

test('Depart is refused with the outstanding list, and there is no way round it', async () => {
  const convoys = serve();
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-1', plate: 'PL-001', driverCount: 2 })]);

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });
  await screen.getByRole('button', { name: 'Depart' }).click();

  const refusal = screen.getByRole('alert');
  await expect.element(refusal).toHaveTextContent('cannot depart until every blocking requirement');
  await expect
    .element(
      refusal.getByRole('list', { name: 'Why it cannot depart' }).getByText('No ferry booking'),
    )
    .toBeInTheDocument();
  expect(convoys.db.get(7)?.departed).toBe(false);
  await expect
    .element(screen.getByRole('button', { name: /anyway|override/i }))
    .not.toBeInTheDocument();
});

test('Depart succeeds once every requirement is met and then is no longer offered', async () => {
  const convoys = serve();
  makeReady(convoys);

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Dispatcher'],
  });
  await screen.getByRole('button', { name: 'Depart' }).click();

  await expect.poll(() => convoys.db.get(7)?.departed).toBe(true);
});

test('only someone who runs a convoy is offered Depart', async () => {
  makeReady(serve());

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Depart' })).not.toBeInTheDocument();
});

test('a convoy that has departed says so and offers no second departure', async () => {
  makeReady(serve());

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} departed />, {
    roles: ['Dispatcher'],
  });

  await expect.element(screen.getByText('This convoy has departed.')).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Depart' })).not.toBeInTheDocument();
});
