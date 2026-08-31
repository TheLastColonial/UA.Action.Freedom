import type { RouteObject } from 'react-router-dom';

import { AppShell } from './components/AppShell';
import { NotFound } from './components/NotFound';
import { RequireAuth } from './components/RequireAuth';
import { RouteError } from './components/RouteError';
import { Dashboard } from './pages/Dashboard';
import { boxRoutes } from './pages/boxes/routes';
import { convoyRoutes } from './pages/convoys/routes';
import { manifestRoutes } from './pages/manifests/routes';
import { peopleRoutes } from './pages/people/routes';
import { receiverRoutes } from './pages/receivers/routes';
import { vehicleRoutes } from './pages/vehicles/routes';

// Every slice is mounted under the authenticated app shell.
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
          { path: 'manifests', children: manifestRoutes },
          { path: 'receivers', children: receiverRoutes },
          { path: '*', element: <NotFound /> },
        ],
      },
    ],
  },
];
