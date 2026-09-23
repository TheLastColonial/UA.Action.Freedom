import { expect, test } from 'vitest';

import { renderWithProviders } from '../test/render';
import { NavSidebar } from './NavSidebar';

test('a ground officer sees only Dashboard and Receivers', async () => {
  const screen = await renderWithProviders(<NavSidebar />, { roles: ['GroundOfficer'] });

  await expect.element(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Receivers' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Vehicles' })).not.toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Manifests' })).not.toBeInTheDocument();
});

test('an operator sees every operational section', async () => {
  const screen = await renderWithProviders(<NavSidebar />, {
    roles: ['Dispatcher', 'Loader', 'Purchaser'],
  });

  for (const name of ['Vehicles', 'Volunteers', 'Convoys', 'Boxes', 'Manifests', 'Receivers']) {
    await expect.element(screen.getByRole('link', { name })).toBeInTheDocument();
  }
});

test('a signed-in user with no roles sees only the Dashboard', async () => {
  const screen = await renderWithProviders(<NavSidebar />, { roles: [] });

  await expect.element(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
  await expect.element(screen.getByRole('link', { name: 'Vehicles' })).not.toBeInTheDocument();
});

test('the sections are nested under the Dashboard, one level down', async () => {
  const screen = await renderWithProviders(<NavSidebar />, { roles: ['Administrator'] });

  const nav = screen.getByRole('navigation', { name: 'Sections' }).element();
  const dashboardItem = [...nav.querySelectorAll(':scope > ul > li')];
  expect(dashboardItem).toHaveLength(1);
  expect(dashboardItem[0]?.querySelector(':scope > a')?.textContent).toBe('Dashboard');

  const nested = dashboardItem[0]?.querySelectorAll(':scope > ul > li > a') ?? [];
  expect([...nested].map((a) => a.textContent)).toEqual([
    'Vehicles',
    'Volunteers',
    'Convoys',
    'Boxes',
    'Manifests',
    'Receivers',
    'Locations',
  ]);
});
