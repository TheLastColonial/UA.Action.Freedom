import { expect, test } from 'vitest';

import { makeConvoy, makeConvoyVehicle } from '../../test/factories/convoy';
import { makeLocation } from '../../test/factories/location';
import { makeManifestBox } from '../../test/factories/manifest';
import { convoyApi } from '../../test/msw/convoys';
import { locationApi } from '../../test/msw/locations';
import { outcomesApi } from '../../test/msw/outcomes';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyOutcomesPanel } from './ConvoyOutcomesPanel';

function serve() {
  const convoys = convoyApi([
    makeConvoy({ id: 7, departed: true, departedAt: '2026-03-01T08:00:00' }),
  ]);
  convoys.vehicles.set(7, [makeConvoyVehicle({ vin: 'VIN-A', plate: 'PL-001' })]);
  convoys.boxes.set('7:VIN-A', [makeManifestBox({ boxId: 5 }), makeManifestBox({ boxId: 6 })]);
  const outcomes = outcomesApi(convoys);
  const locations = locationApi([
    makeLocation({ id: 1, name: 'Coventry hub', isRegisteredHub: true }),
    makeLocation({ id: 2, name: 'A garage', isRegisteredHub: false }),
  ]);
  worker.use(...convoys.handlers, ...outcomes.handlers, ...locations.handlers);
  return { convoys, outcomes };
}

const render = (departed = true) =>
  renderWithProviders(<ConvoyOutcomesPanel convoyId={7} departed={departed} />, {
    roles: ['Dispatcher'],
  });

test('waits for departure before offering any outcome', async () => {
  serve();

  const screen = await render(false);

  await expect.element(screen.getByText(/once the convoy has departed/)).toBeInTheDocument();
});

test('lists each box on the vehicle with no outcome yet', async () => {
  serve();

  const screen = await render();

  await expect.element(screen.getByText('#5')).toBeInTheDocument();
  await expect.element(screen.getByText('#6')).toBeInTheDocument();
  expect(screen.getByText('No outcome yet').elements()).toHaveLength(2);
});

test('records a seized box with the outcome the Dispatcher chose', async () => {
  const { outcomes } = serve();
  const screen = await render();

  await screen.getByLabelText('Outcome for box 5').selectOptions('Seized');
  await screen
    .getByRole('form', { name: 'Record an outcome for box 5' })
    .getByRole('button', { name: 'Record' })
    .click();

  await expect.element(screen.getByRole('cell', { name: 'Seized' })).toBeInTheDocument();
  expect(outcomes.recorded).toEqual([{ boxId: 5, body: { outcome: 'Seized' } }]);
});

test('an undeliverable box is sent with one of the bounded reasons', async () => {
  const { outcomes } = serve();
  const screen = await render();

  await screen.getByLabelText('Outcome for box 5').selectOptions('Undeliverable');
  await screen.getByLabelText('Reason for box 5').selectOptions('stolen');
  await screen
    .getByRole('form', { name: 'Record an outcome for box 5' })
    .getByRole('button', { name: 'Record' })
    .click();

  await expect.element(screen.getByText('Undeliverable (stolen)')).toBeInTheDocument();
  expect(outcomes.recorded[0]?.body).toEqual({ outcome: 'Undeliverable', reason: 'stolen' });
});

test('a box returned to a hub offers only the registered hubs, and needs one chosen', async () => {
  const { outcomes } = serve();
  const screen = await render();

  await screen.getByLabelText('Outcome for box 5').selectOptions('ReturnedToHub');

  await expect.element(screen.getByRole('option', { name: 'Coventry hub' })).toBeInTheDocument();
  expect(screen.getByRole('option', { name: 'A garage' }).elements()).toHaveLength(0);
  await expect
    .element(
      screen
        .getByRole('form', { name: 'Record an outcome for box 5' })
        .getByRole('button', { name: 'Record' }),
    )
    .toBeDisabled();

  await screen.getByLabelText('Hub for box 5').selectOptions('1');
  await screen
    .getByRole('form', { name: 'Record an outcome for box 5' })
    .getByRole('button', { name: 'Record' })
    .click();

  await expect.element(screen.getByRole('cell', { name: 'Returned to a hub' })).toBeInTheDocument();
  expect(outcomes.recorded[0]?.body).toEqual({ outcome: 'ReturnedToHub', hubLocationId: 1 });
});

test('a seized box offers no further outcome', async () => {
  const { outcomes } = serve();
  const screen = await render();
  await screen.getByLabelText('Outcome for box 5').selectOptions('Seized');
  await screen
    .getByRole('form', { name: 'Record an outcome for box 5' })
    .getByRole('button', { name: 'Record' })
    .click();
  await expect.element(screen.getByRole('cell', { name: 'Seized' })).toBeInTheDocument();

  expect(screen.getByLabelText('Outcome for box 5').elements()).toHaveLength(0);
  expect(outcomes.outcomes.get('7:5')?.outcome).toBe('Seized');
});

test('marking the vehicle delivered delivers its boxes', async () => {
  const { outcomes } = serve();
  const screen = await render();

  await screen.getByRole('button', { name: 'Mark PL-001 delivered' }).click();

  await expect.element(screen.getByText('Delivered.')).toBeInTheDocument();
  expect(outcomes.outcomes.get('7:5')?.outcome).toBe('Delivered');
  expect(outcomes.outcomes.get('7:6')?.outcome).toBe('Delivered');
});

test('accepts a box that has been delivered', async () => {
  const { outcomes } = serve();
  outcomes.outcomes.set('7:5', {
    boxId: 5,
    convoyId: 7,
    vin: 'VIN-A',
    outcome: 'Delivered',
    reason: null,
    hubLocationId: null,
    recordedAt: '2026-03-04T12:00:00',
    recordedByName: null,
  });
  const screen = await render();

  await screen.getByLabelText('Outcome for box 5').selectOptions('Accepted');
  await screen
    .getByRole('form', { name: 'Record an outcome for box 5' })
    .getByRole('button', { name: 'Record' })
    .click();

  await expect.element(screen.getByText('Accepted')).toBeInTheDocument();
});

test('a Loader sees the outcomes but cannot record them', async () => {
  serve();

  const screen = await renderWithProviders(<ConvoyOutcomesPanel convoyId={7} departed />, {
    roles: ['Loader'],
  });

  await expect.element(screen.getByText('#5')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Record' }).elements()).toHaveLength(0);
  expect(screen.getByRole('button', { name: 'Mark PL-001 delivered' }).elements()).toHaveLength(0);
});
