import { expect, test } from 'vitest';

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

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7, start: '2026-09-01T06:00:00' })]);
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

test('a convoy with a route and every vehicle crewed and insured is ready', async () => {
  const convoys = serve();
  convoys.routes.set(7, [makeRouteStop()]);
  convoys.vehicles.set(7, [
    makeConvoyVehicle({ vin: 'VIN-1', plate: 'PL-001', ukDriverCount: 2, borderDriverCount: 2 }),
  ]);
  convoys.insurance.set('7:VIN-1', insured('VIN-1'));

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
});

test('says what each vehicle still needs, and that it is advisory', async () => {
  const convoys = serve();
  convoys.vehicles.set(7, [
    // Crewed for one leg only: the reasons have to say which half still needs drivers.
    makeConvoyVehicle({
      vin: 'VIN-1',
      plate: 'PL-001',
      ukDriverCount: 1,
      ukPassengerCount: 2,
      borderDriverCount: 0,
    }),
    makeConvoyVehicle({
      vin: 'VIN-2',
      plate: 'PL-002',
      ukDriverCount: 2,
      borderDriverCount: 2,
    }),
  ]);
  convoys.insurance.set('7:VIN-2', {
    ...insured('VIN-2'),
    voided: true,
    voidedAt: '2026-08-30T00:00:00',
  });

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not ready yet' })).toBeInTheDocument();
  await expect.element(screen.getByText('No route planned')).toBeInTheDocument();
  await expect
    .element(
      screen.getByText(
        'PL-001: Fewer than two drivers on the UK to Europe leg; Fewer than two drivers on the Europe to Ukraine leg; Insurance not recorded',
      ),
    )
    .toBeInTheDocument();
  await expect
    .element(screen.getByText('PL-002: Insurance voided by a crew change'))
    .toBeInTheDocument();
  await expect.element(screen.getByText(/advisory/)).toBeInTheDocument();
});

test('a withdrawn vehicle is not reported as unready', async () => {
  // It broke down and left: it has no crew to find and no insurance to renew, so holding the
  // convoy open for it would be reporting a problem nobody can fix.
  const convoys = serve();
  convoys.routes.set(7, [makeRouteStop()]);
  convoys.vehicles.set(7, [
    makeConvoyVehicle({ vin: 'VIN-1', plate: 'PL-001', ukDriverCount: 2, borderDriverCount: 2 }),
    makeWithdrawnConvoyVehicle({ vin: 'VIN-2', plate: 'PL-002' }),
  ]);
  convoys.insurance.set('7:VIN-1', insured('VIN-1'));

  const screen = await renderWithProviders(<ConvoyReadinessPanel convoyId={7} />, {
    roles: ['Loader'],
  });

  await expect
    .element(screen.getByRole('heading', { name: 'Ready to travel' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText(/PL-002/)).not.toBeInTheDocument();
});
