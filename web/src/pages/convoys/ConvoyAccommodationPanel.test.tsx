import { expect, test } from 'vitest';

import type { Role } from '../../auth/roles';
import { makeConvoy } from '../../test/factories/convoy';
import { accommodationApi } from '../../test/msw/accommodation';
import { convoyApi } from '../../test/msw/convoys';
import { worker } from '../../test/msw/worker';
import { renderWithProviders } from '../../test/render';
import { ConvoyAccommodationPanel } from './ConvoyAccommodationPanel';

const ANNA = '00000000-0000-0000-0000-00000000000a';
const BORIS = '00000000-0000-0000-0000-00000000000b';

function serve() {
  const convoys = convoyApi([makeConvoy({ id: 7 })]);
  const api = accommodationApi({
    crew: [
      { personId: ANNA, name: 'Anna One' },
      { personId: BORIS, name: 'Boris Two' },
    ],
    stops: [
      { routePointId: 1, sequence: 1, name: 'Lille' },
      { routePointId: 2, sequence: 3, name: 'Reims' },
    ],
  });
  worker.use(...api.handlers, ...convoys.handlers);
  return api;
}

function renderPanel(role: Role = 'Dispatcher') {
  return renderWithProviders(<ConvoyAccommodationPanel convoyId={7} />, { roles: [role] });
}

test('shows every crew member at every overnight stop, and how many cells are missing', async () => {
  const api = serve();
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
    guests: [ANNA, BORIS],
    lastChangedByName: null,
    lastChangedAt: null,
  });
  api.selfArranged.push({
    convoyId: 7,
    routePointId: 2,
    personId: ANNA,
    lastChangedByName: null,
    lastChangedAt: null,
  });

  const screen = await renderPanel();

  await expect
    .element(screen.getByRole('cell', { name: 'Anna One at Lille: Booked' }))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('cell', { name: 'Boris Two at Lille: Booked' }))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('cell', { name: 'Anna One at Reims: Arranging their own' }))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('cell', { name: 'Boris Two at Reims: Missing' }))
    .toBeInTheDocument();
  await expect.element(screen.getByText('1 night is not covered.')).toBeInTheDocument();
});

test('says so when everyone is covered at every overnight stop', async () => {
  const api = serve();
  api.stops.length = 0;

  const screen = await renderPanel();

  await expect
    .element(screen.getByText('Every crew member is covered at every overnight stop.'))
    .toBeInTheDocument();
});

test('a dispatcher books one shared room and both cells read booked', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Stop').selectOptions('1');
  await screen.getByLabelText('Provider').fill('Ibis Lille');
  await screen.getByLabelText('Check in').fill('2026-09-02');
  await screen.getByLabelText('Check out').fill('2026-09-03');
  await screen.getByLabelText('Cost (£)').fill('120');
  await screen.getByLabelText('Anna One').click();
  await screen.getByLabelText('Boris Two').click();
  await screen.getByRole('button', { name: 'Book accommodation' }).click();

  await expect
    .element(screen.getByRole('cell', { name: 'Anna One at Lille: Booked' }))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('cell', { name: 'Boris Two at Lille: Booked' }))
    .toBeInTheDocument();
  expect(api.bookings).toHaveLength(1);
  expect(api.bookings[0]?.guests).toEqual([ANNA, BORIS]);
  expect(api.bookings[0]?.costGbp).toBe(120);
});

test('a booking needs a provider and at least one guest before anything is sent', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Check in').fill('2026-09-02');
  await screen.getByLabelText('Check out').fill('2026-09-03');
  await screen.getByRole('button', { name: 'Book accommodation' }).click();

  await expect.element(screen.getByText('Enter the provider')).toBeInTheDocument();
  await expect.element(screen.getByText('Choose at least one crew member')).toBeInTheDocument();
  expect(api.bookings).toHaveLength(0);
});

test('check out cannot be before check in', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen.getByLabelText('Provider').fill('Ibis Lille');
  await screen.getByLabelText('Check in').fill('2026-09-03');
  await screen.getByLabelText('Check out').fill('2026-09-02');
  await screen.getByLabelText('Anna One').click();
  await screen.getByRole('button', { name: 'Book accommodation' }).click();

  await expect.element(screen.getByText('Check out cannot be before check in')).toBeInTheDocument();
  expect(api.bookings).toHaveLength(0);
});

test('a missing cell can be marked as arranging their own, and cleared again', async () => {
  const api = serve();
  const screen = await renderPanel();

  await screen
    .getByRole('button', { name: 'Mark Anna One as arranging their own at Lille' })
    .click();

  await expect
    .element(screen.getByRole('cell', { name: 'Anna One at Lille: Arranging their own' }))
    .toBeInTheDocument();
  expect(api.selfArranged).toHaveLength(1);

  await screen.getByRole('button', { name: 'Clear Anna One arranging their own at Lille' }).click();

  await expect
    .element(screen.getByRole('cell', { name: 'Anna One at Lille: Missing' }))
    .toBeInTheDocument();
  expect(api.selfArranged).toHaveLength(0);
});

test('a booking left behind by a crew member who left is listed as a warning', async () => {
  const api = serve();
  api.bookings.push({
    id: 4,
    convoyId: 7,
    routePointId: 1,
    provider: 'Ibis Lille',
    reference: null,
    checkIn: '2026-09-02',
    checkOut: '2026-09-03',
    details: null,
    costGbp: null,
    cancelled: false,
    guests: ['someone-who-left'],
    lastChangedByName: null,
    lastChangedAt: null,
  });

  const screen = await renderPanel();

  await expect
    .element(screen.getByText(/Booking 4 at Lille covers nobody still crewed/))
    .toBeInTheDocument();
});

test('bookings are listed with their guests and a cancelled one says so', async () => {
  const api = serve();
  api.bookings.push(
    {
      id: 1,
      convoyId: 7,
      routePointId: 1,
      provider: 'Ibis Lille',
      reference: 'REF-1',
      checkIn: '2026-09-02',
      checkOut: '2026-09-03',
      details: null,
      costGbp: 120,
      cancelled: false,
      guests: [ANNA],
      lastChangedByName: null,
      lastChangedAt: null,
    },
    {
      id: 2,
      convoyId: 7,
      routePointId: 2,
      provider: 'Hotel Reims',
      reference: null,
      checkIn: '2026-09-03',
      checkOut: '2026-09-04',
      details: null,
      costGbp: null,
      cancelled: true,
      guests: [BORIS],
      lastChangedByName: null,
      lastChangedAt: null,
    },
  );

  const screen = await renderPanel();

  await expect.element(screen.getByText(/Ibis Lille at Lille/)).toBeInTheDocument();
  await expect.element(screen.getByText(/Hotel Reims at Reims.*cancelled/i)).toBeInTheDocument();
});

test('a loader sees the grid but cannot book or mark anything', async () => {
  serve();

  const screen = await renderPanel('Loader');

  await expect
    .element(screen.getByRole('cell', { name: 'Anna One at Lille: Missing' }))
    .toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: 'Book accommodation' }))
    .not.toBeInTheDocument();
  await expect
    .element(screen.getByRole('button', { name: /Mark Anna One as arranging/ }))
    .not.toBeInTheDocument();
});
