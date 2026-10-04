import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy, makeConvoyVehicle, makeVehicleCrew } from '../../test/factories/convoy';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyLeaderPanel } from './ConvoyLeaderPanel';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-1', plate: 'PL-001' })]);
  convoys.crew.set('7:VIN-1', [
    makeVehicleCrew({ personId: 'p-olena', firstName: 'Olena', lastName: 'Bondar' }),
    makeVehicleCrew({ personId: 'p-taras', firstName: 'Taras', lastName: 'Melnyk' }),
    makeVehicleCrew({
      personId: 'p-ivana',
      firstName: 'Ivana',
      lastName: 'Kovalenko',
      role: 'Passenger',
    }),
  ]);
  worker.use(...convoys.handlers);
  return convoys;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<ConvoyLeaderPanel convoyId={7} arrived={false} />, {
    roles: [role],
  });
}

test('says when nobody leads the convoy yet', async () => {
  serve();
  const screen = await renderPanel();

  await expect.element(screen.getByText('No leader nominated yet.')).toBeInTheDocument();
});

test('a dispatcher nominates a crewed driver, never a passenger, and reassigns with history', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await expect
    .element(screen.getByRole('option', { name: 'Ivana Kovalenko' }))
    .not.toBeInTheDocument();

  await screen.getByLabelText('Nominate leader').selectOptions('p-olena');
  await screen.getByRole('button', { name: 'Nominate' }).click();
  await expect.element(screen.getByText('Olena Bondar leads this convoy.')).toBeInTheDocument();

  await screen.getByLabelText('Nominate leader').selectOptions('p-taras');
  await screen.getByRole('button', { name: 'Nominate' }).click();
  await expect.element(screen.getByText('Taras Melnyk leads this convoy.')).toBeInTheDocument();

  expect(convoys.leaders.get(7)?.map((a) => a.personName)).toEqual([
    'Taras Melnyk',
    'Olena Bondar',
  ]);
  await expect.element(screen.getByRole('table', { name: 'Leader history' })).toBeInTheDocument();
});

test('an operator without the policy can see the leader but not nominate', async () => {
  serve();
  const screen = await renderPanel('Loader');

  await expect.element(screen.getByText('No leader nominated yet.')).toBeInTheDocument();
  await expect.element(screen.getByRole('button', { name: 'Nominate' })).not.toBeInTheDocument();
});
