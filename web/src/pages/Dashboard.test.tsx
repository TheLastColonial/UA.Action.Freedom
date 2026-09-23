import { expect, test } from 'vitest';

import { renderWithProviders } from '../test/render';
import { Dashboard } from './Dashboard';

test('an administrator sees a card linking to every operational section', async () => {
  const screen = await renderWithProviders(<Dashboard />, { roles: ['Administrator'] });

  const expected: Record<string, string> = {
    Vehicles: '/vehicles',
    Volunteers: '/people',
    Convoys: '/convoys',
    Boxes: '/boxes',
    Manifests: '/manifests',
    Receivers: '/receivers',
    Locations: '/locations',
  };

  for (const [name, href] of Object.entries(expected)) {
    await expect.element(screen.getByRole('link', { name })).toHaveAttribute('href', href);
  }
});

test('a ground officer, who can only read receivers, sees only the Receivers card', async () => {
  const screen = await renderWithProviders(<Dashboard />, { roles: ['GroundOfficer'] });

  await expect.element(screen.getByRole('link', { name: 'Receivers' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Vehicles' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Volunteers' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Convoys' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Boxes' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Manifests' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Locations' })).not.toBeInTheDocument();
});

test('a signed-in user with no roles sees no section cards', async () => {
  const screen = await renderWithProviders(<Dashboard />, { roles: [] });

  for (const name of [
    'Vehicles',
    'Volunteers',
    'Convoys',
    'Boxes',
    'Manifests',
    'Receivers',
    'Locations',
  ]) {
    await expect.element(screen.getByRole('link', { name })).not.toBeInTheDocument();
  }
});

test('does not display the signed-in identity GUID anywhere on the page', async () => {
  const screen = await renderWithProviders(<Dashboard />, {
    roles: ['Administrator'],
    sub: '11111111-2222-3333-4444-555555555555',
  });

  await expect.element(screen.getByText(/signed in/i)).not.toBeInTheDocument();
  await expect
    .element(screen.getByText('11111111-2222-3333-4444-555555555555'))
    .not.toBeInTheDocument();
});

test('cards are navigation only: creating something happens on its section page', async () => {
  const screen = await renderWithProviders(<Dashboard />, { roles: ['Administrator'] });

  for (const name of [
    'New Vehicle',
    'New Volunteer',
    'New Convoy',
    'New Box',
    'New Manifest',
    'New Receiver',
    'New Location',
  ]) {
    await expect.element(screen.getByRole('link', { name })).not.toBeInTheDocument();
  }
});
