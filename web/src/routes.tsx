import type { RouteObject } from 'react-router-dom';

import { AppShell } from './components/AppShell';
import { NotFound } from './components/NotFound';
import { RequireAuth } from './components/RequireAuth';
import { RouteError } from './components/RouteError';
import { Dashboard } from './pages/Dashboard';
import { Placeholder } from './pages/Placeholder';
import { boxRoutes } from './pages/boxes/routes';
import { convoyRoutes } from './pages/convoys/routes';
import { peopleRoutes } from './pages/people/routes';
import { vehicleRoutes } from './pages/vehicles/routes';

// Slice routes are placeholders until Phases C–G replace them with the real pages.
export const routes: RouteObject[] = [
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        errorElement: <RouteError />,
        children: [
          { index: true, element: <Dashboard /> },
          { path: 'vehicles', children: vehicleRoutes },
          { path: 'people', children: peopleRoutes },
          { path: 'convoys', children: convoyRoutes },
          { path: 'boxes', children: boxRoutes },
          { path: 'manifests/*', element: <Placeholder title="Manifests" /> },
          { path: 'receivers/*', element: <Placeholder title="Receivers" /> },
          { path: '*', element: <NotFound /> },
        ],
      },
    ],
  },
];
