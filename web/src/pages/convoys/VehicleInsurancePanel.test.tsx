import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy, makeConvoyVehicle, makeInsurance } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { VehicleInsurancePanel } from './VehicleInsurancePanel';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' })]);
  worker.use(...convoys.handlers);
  return convoys;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(
    <VehicleInsurancePanel convoyId={7} vin="VIN-TEST-1" plate="PL-001" />,
    {
      roles: [role],
    },
  );
}

test('says when no insurance has been recorded', async () => {
  serve();

  const screen = await renderPanel();

  await expect.element(screen.getByText('Insurance not recorded')).toBeInTheDocument();
});

test('a dispatcher records insurance and it is stored for the vehicle', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Insurer').fill('Ukraine Aid Mutual');
  await screen.getByLabelText('Policy number').fill('POL-9');
  await screen.getByLabelText('Cover starts').fill('2026-08-25');
  await screen.getByLabelText('Cover ends').fill('2026-09-30');
  await screen.getByLabelText('Cost (£)').fill('412.50');
  await screen.getByRole('button', { name: 'Record insurance' }).click();

  await expect.element(screen.getByText(/Insured with Ukraine Aid Mutual/)).toBeInTheDocument();
  expect(convoys.insurance.get('7:VIN-TEST-1')).toMatchObject({
    insurer: 'Ukraine Aid Mutual',
    policyNumber: 'POL-9',
    costGbp: 412.5,
    voided: false,
  });
});

test('cover that ends before it starts is refused before anything is sent', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Insurer').fill('Ukraine Aid Mutual');
  await screen.getByLabelText('Policy number').fill('POL-9');
  await screen.getByLabelText('Cover starts').fill('2026-09-30');
  await screen.getByLabelText('Cover ends').fill('2026-08-25');
  await screen.getByRole('button', { name: 'Record insurance' }).click();

  await expect.element(screen.getByText('Cover cannot end before it starts')).toBeInTheDocument();
  expect(convoys.insurance.has('7:VIN-TEST-1')).toBe(false);
});

test('insurance voided by a crew change says it must be recorded again', async () => {
  const convoys = serve();
  convoys.insurance.set(
    '7:VIN-TEST-1',
    makeInsurance({
      convoyId: 7,
      vin: 'VIN-TEST-1',
      voided: true,
      voidedAt: '2026-09-01T10:00:00',
    }),
  );

  const screen = await renderPanel();

  await expect
    .element(screen.getByRole('status'))
    .toHaveTextContent('voided by a crew change — record it again');
});

test('a role that cannot record insurance sees its state but no form', async () => {
  const convoys = serve();
  convoys.insurance.set('7:VIN-TEST-1', makeInsurance({ convoyId: 7, vin: 'VIN-TEST-1' }));

  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText(/Insured with Ukraine Aid Mutual/)).toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Record insurance' }))
    .not.toBeInTheDocument();
});
