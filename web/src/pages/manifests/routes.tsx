import type { RouteObject } from 'react-router-dom';

import { ManifestDetailPage } from './ManifestDetailPage';
import { ManifestEditPage } from './ManifestEditPage';
import { ManifestsListPage } from './ManifestsListPage';

export const manifestRoutes: RouteObject[] = [
  { index: true, element: <ManifestsListPage /> },
  { path: ':id', element: <ManifestDetailPage /> },
  { path: ':id/edit', element: <ManifestEditPage /> },
];
