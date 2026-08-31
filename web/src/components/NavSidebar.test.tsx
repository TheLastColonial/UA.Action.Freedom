import { expect, test } from 'vitest';

import { renderWithProviders } from '../test/render';
import { NavSidebar } from './NavSidebar';

test('a ground officer sees only Dashboard and Receivers', async () => {
  const screen = renderWithProviders(<NavSidebar />, { roles: ['GroundOfficer'] });

  await expect.element(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Receivers' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Vehicles' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Manifests' })).not.toBeInTheDocument();
});

test('an operator sees every operational section', async () => {
  const screen = renderWithProviders(<NavSidebar />, {
    roles: ['Dispatcher', 'Loader', 'Purchaser'],
  });

  for (const name of ['Vehicles', 'Volunteers', 'Convoys', 'Boxes', 'Manifests', 'Receivers']) {
    await expect.element(screen.getByRole('link', { name })).toBeInTheDocument();
  }
});

test('a signed-in user with no roles sees only the Dashboard', async () => {
  const screen = renderWithProviders(<NavSidebar />, { roles: [] });

  await expect.element(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Vehicles' })).not.toBeInTheDocument();
});
