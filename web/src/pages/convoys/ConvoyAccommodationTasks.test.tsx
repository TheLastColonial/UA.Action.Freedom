import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy } from '../../test/factories/convoy';
import { accommodationApi } from '../../test/msw/accommodation';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyAccommodationTasks } from './ConvoyAccommodationTasks';

const ANNA = '00000000-0000-0000-0000-00000000000a';
const BORIS = '00000000-0000-0000-0000-00000000000b';
const CARLA = '00000000-0000-0000-0000-00000000000c';

// Anna had the booking and has left the crew; Boris and Carla are crewed now.
function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  const api = accommodationApi({
    crew: [
      { personId: BORIS, name: 'Boris Two' },
      { personId: CARLA, name: 'Carla Three' },
    ],
    stops: [{ routePointId: 1, sequence: 1, name: 'Lille' }],
  });
  api.bookings.push({
    id: 1,
    convoyId: 7,
    routePointId: 1,
    provider: 'Ibis Lille',
    reference: null,
    checkIn: '2026-09-02',
    checkOut: '2026-09-03',
    details: null,
    costGbp: 120,
    cancelled: false,
    guests: [ANNA],
    lastChangedByName: null,
    lastChangedAt: null,
  });
  // The convoy fake answers an empty task list, so the accommodation handlers go first.
  worker.use(...api.handlers, ...convoys.handlers);
  return api;
}

function renderTasks(role: Role = 'Dispatcher') {
  return renderWithProviders(<ConvoyAccommodationTasks convoyId={7} />, { roles: [role] });
}

test('a booking whose guest left the crew is a task naming the booking and its stop', async () => {
  serve();

  const screen = await renderTasks();

  await expect
    .element(screen.getByText(/Booking 1 at Lille covers nobody still crewed/))
    .toBeInTheDocument();
});

test('says so when no booking needs dealing with', async () => {
  const api = serve();
  api.bookings.length = 0;

  const screen = await renderTasks();

  await expect
    .element(screen.getByText('No accommodation needs dealing with.'))
    .toBeInTheDocument();
});

test('cancelling the booking clears the task', async () => {
  const api = serve();
  const screen = await renderTasks();

  await screen.getByRole('button', { name: 'Cancel booking 1' }).click();

  await expect
    .element(screen.getByText('No accommodation needs dealing with.'))
    .toBeInTheDocument();
  expect(api.bookings[0]?.cancelled).toBe(true);
});

test('migrating the place to a replacement clears the task and puts them on the booking', async () => {
  const api = serve();
  const screen = await renderTasks();

  await screen.getByLabelText('Replacement for guest 1 of booking 1').selectOptions(CARLA);
  await screen.getByRole('button', { name: 'Migrate guest 1 of booking 1' }).click();

  await expect
    .element(screen.getByText('No accommodation needs dealing with.'))
    .toBeInTheDocument();
  expect(api.bookings[0]?.guests).toEqual([CARLA]);
});

test('a loader sees nothing to act on', async () => {
  serve();

  const screen = await renderTasks('Loader');

  await expect
    .element(screen.getByRole('button', { name: 'Cancel booking 1' }))
    .not.toBeInTheDocument();
});
