import type { RouteObject } from 'react-router-dom';

import { LocationCreatePage } from './LocationCreatePage';
import { LocationDetailPage } from './LocationDetailPage';
import { LocationEditPage } from './LocationEditPage';
import { LocationsListPage } from './LocationsListPage';

export const locationRoutes: RouteObject[] = [
  { index: true, element: <LocationsListPage /> },
  { path: 'new', element: <LocationCreatePage /> },
  { path: ':id', element: <LocationDetailPage /> },
  { path: ':id/edit', element: <LocationEditPage /> },
];
