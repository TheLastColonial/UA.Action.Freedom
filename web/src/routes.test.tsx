import { expect, test } from 'vitest';

import { renderWithProviders } from './test/render';
import { routes } from './routes';

test('the shell renders the dashboard at the index route', async () => {
  const screen = renderWithProviders(null, { routes, route: '/', roles: ['Administrator'] });

  await expect.element(screen.getByRole('link', { name: 'UA Action Freedom' })).toBeInTheDocument();
  await expect.element(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument();
});

test('an unknown path under the shell renders Not found', async () => {
  const screen = renderWithProviders(null, {
    routes,
    route: '/nonsense',
    roles: ['Administrator'],
  });

  await expect.element(screen.getByRole('heading', { name: 'Not found' })).toBeInTheDocument();
});

test('a section the role cannot read is absent from the nav', async () => {
  const screen = renderWithProviders(null, { routes, route: '/', roles: ['GroundOfficer'] });
  const nav = screen.getByRole('navigation', { name: 'Sections' });

  await expect.element(nav.getByRole('link', { name: 'Receivers' })).toBeInTheDocument();
  await expect.element(nav.getByRole('link', { name: 'Manifests' })).not.toBeInTheDocument();
});
