import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import {
  makeConvoy,
  makeConvoyVehicle,
  makeInsurance,
  makeVehicleCrew,
  makeWithdrawnConvoyVehicle,
} from '../../test/factories/convoy';
import { makePerson } from '../../test/factories/person';
import { convoyApi } from '../../test/msw/convoys';
import { personApi } from '../../test/msw/people';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyDriversPanel } from './ConvoyDriversPanel';

const alice = makePerson({ firstName: 'Alice', lastName: 'Driver', isDriver: true });
const bob = makePerson({ firstName: 'Bob', lastName: 'Walker', isDriver: false });

function serve() {
  const people = personApi([alice, bob]);
  const convoys = convoyApi([makeConvoy({ id: 7 })], { people: people.db });
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' })]);
  worker.use(...convoys.handlers, ...people.handlers);
  return convoys;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<ConvoyDriversPanel convoyId={7} />, { roles: [role] });
}

test('says when the convoy has no vehicles to crew', async () => {
  const convoys = serve();
  convoys.vehicles.set(7, []);

  const screen = await renderPanel();

  await expect
    .element(screen.getByText('Put vehicles on the truck list before assigning drivers.'))
    .toBeInTheDocument();
});

test('a dispatcher crews a vehicle with a registered driver', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Add driver to PL-001').selectOptions(alice.id);
  await screen.getByRole('button', { name: 'Assign' }).click();

  await expect.element(screen.getByRole('cell', { name: 'Alice Driver' })).toBeInTheDocument();
  await expect
    .element(screen.getByRole('cell', { name: 'Driver', exact: true }))
    .toBeInTheDocument();
  expect(convoys.crew.get('7:VIN-TEST-1')?.map((d) => [d.personId, d.leg, d.role])).toEqual([
    [alice.id, 'Uk', 'Driver'],
  ]);
  await expect
    .element(screen.getByRole('option', { name: 'Alice Driver' }))
    .not.toBeInTheDocument();
});

test('any volunteer may ride as a passenger', async () => {
  const convoys = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Role on PL-001').selectOptions('Passenger');
  await screen.getByLabelText('Add passenger to PL-001').selectOptions(bob.id);
  await screen.getByRole('button', { name: 'Assign' }).click();

  await expect.element(screen.getByRole('cell', { name: 'Bob Walker' })).toBeInTheDocument();
  await expect.element(screen.getByRole('cell', { name: 'Passenger' })).toBeInTheDocument();
  expect(convoys.crew.get('7:VIN-TEST-1')?.map((d) => d.role)).toEqual(['Passenger']);
});

test('a person already crewing another vehicle on the same leg is refused, with the reason', async () => {
  const convoys = serve();
  convoys.vehicles.set(7, [
    makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' }),
    makeConvoyVehicle({ vin: 'VIN-TEST-2', plate: 'PL-002', ukDriverCount: 1 }),
  ]);
  convoys.crew.set('7:VIN-TEST-2', [
    makeVehicleCrew({ personId: alice.id, firstName: 'Alice', lastName: 'Driver' }),
  ]);
  const screen = await renderPanel();

  await screen.getByLabelText('Add driver to PL-001').selectOptions(alice.id);
  await screen.getByRole('button', { name: 'Assign' }).first().click();

  await expect.element(screen.getByRole('alert')).toHaveTextContent('another vehicle on this leg');
  expect(convoys.crew.get('7:VIN-TEST-1') ?? []).toEqual([]);
});

test('the same volunteer may take the border leg of another vehicle', async () => {
  // The handover at the European border is exactly what the leg exists to record: one seat per
  // person per leg, not per convoy.
  const convoys = serve();
  convoys.vehicles.set(7, [
    makeConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' }),
    makeConvoyVehicle({ vin: 'VIN-TEST-2', plate: 'PL-002', ukDriverCount: 1 }),
  ]);
  convoys.crew.set('7:VIN-TEST-2', [
    makeVehicleCrew({ personId: alice.id, firstName: 'Alice', lastName: 'Driver', leg: 'Uk' }),
  ]);
  const screen = await renderPanel();

  await screen.getByLabelText('Leg for PL-001').selectOptions('Border');
  await screen.getByLabelText('Add driver to PL-001').selectOptions(alice.id);
  await screen.getByRole('button', { name: 'Assign' }).first().click();

  await expect.element(screen.getByRole('cell', { name: 'Europe to Ukraine' })).toBeInTheDocument();
  expect(convoys.crew.get('7:VIN-TEST-1')?.map((d) => d.leg)).toEqual(['Border']);
});

test('a withdrawn vehicle shows why it left and takes no crew', async () => {
  const convoys = serve();
  convoys.vehicles.set(7, [makeWithdrawnConvoyVehicle({ vin: 'VIN-TEST-1', plate: 'PL-001' })]);

  const screen = await renderPanel();

  await expect.element(screen.getByText(/Gearbox failure near Poznan/)).toBeInTheDocument();
  await expect.element(screen.getByLabelText('Add driver to PL-001')).not.toBeInTheDocument();
});

test('only registered drivers are offered', async () => {
  serve();
  const screen = await renderPanel();

  await expect.element(screen.getByRole('option', { name: 'Alice Driver' })).toBeInTheDocument();
  await expect.element(screen.getByRole('option', { name: 'Bob Walker' })).not.toBeInTheDocument();
});

test('a dispatcher stands a driver down', async () => {
  const convoys = serve();
  convoys.crew.set('7:VIN-TEST-1', [
    makeVehicleCrew({ personId: alice.id, firstName: 'Alice', lastName: 'Driver' }),
  ]);
  const screen = await renderPanel();

  await screen
    .getByRole('button', { name: 'Remove Alice Driver from the UK to Europe leg' })
    .click();

  await expect.element(screen.getByText('No crew assigned yet')).toBeInTheDocument();
  expect(convoys.crew.get('7:VIN-TEST-1')).toEqual([]);
});

test('shows the reason when the API refuses the driver', async () => {
  const convoys = serve();
  const screen = await renderPanel();
  await expect.element(screen.getByLabelText('Add driver to PL-001')).toBeInTheDocument();

  // Another dispatcher takes the vehicle off the truck list while this page is open.
  convoys.vehicles.set(7, []);

  await screen.getByLabelText('Add driver to PL-001').selectOptions(alice.id);
  await screen.getByRole('button', { name: 'Assign' }).click();

  await expect.element(screen.getByRole('alert')).toHaveTextContent('on this convoy');
});

test.each<[Role]>([['Loader'], ['Administrator'], ['Purchaser']])(
  'a %s sees the crew but cannot change it',
  async (role) => {
    const convoys = serve();
    convoys.crew.set('7:VIN-TEST-1', [
      makeVehicleCrew({ personId: alice.id, firstName: 'Alice', lastName: 'Driver' }),
    ]);

    const screen = await renderPanel(role);

    await expect.element(screen.getByRole('cell', { name: 'Alice Driver' })).toBeInTheDocument();
    await expect.element(screen.getByLabelText('Add driver to PL-001')).not.toBeInTheDocument();
    await expect
      .element(screen.getByRole('button', { name: /^Remove Alice Driver/ }))
      .not.toBeInTheDocument();
  },
);

test('changing the crew after insuring the vehicle shows the policy as voided', async () => {
  const convoys = serve();
  convoys.insurance.set('7:VIN-TEST-1', makeInsurance({ convoyId: 7, vin: 'VIN-TEST-1' }));
  const screen = await renderPanel();
  await expect.element(screen.getByText(/Insured with Ukraine Aid Mutual/)).toBeInTheDocument();

  await screen.getByLabelText('Add driver to PL-001').selectOptions(alice.id);
  await screen.getByRole('button', { name: 'Assign' }).click();

  await expect.element(screen.getByText(/voided by a crew change/)).toBeInTheDocument();
});
