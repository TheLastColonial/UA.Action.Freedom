import type { RouteObject } from 'react-router-dom';

import { AppShell } from './components/AppShell';
import { NotFound } from './components/NotFound';
import { RequireAuth } from './components/RequireAuth';
import { RouteError } from './components/RouteError';
import { Dashboard } from './pages/Dashboard';
import { Placeholder } from './pages/Placeholder';

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
          { path: 'vehicles/*', element: <Placeholder title="Vehicles" /> },
          { path: 'people/*', element: <Placeholder title="Volunteers" /> },
          { path: 'convoys/*', element: <Placeholder title="Convoys" /> },
          { path: 'boxes/*', element: <Placeholder title="Boxes" /> },
          { path: 'manifests/*', element: <Placeholder title="Manifests" /> },
          { path: 'receivers/*', element: <Placeholder title="Receivers" /> },
          { path: '*', element: <NotFound /> },
        ],
      },
    ],
  },
];
