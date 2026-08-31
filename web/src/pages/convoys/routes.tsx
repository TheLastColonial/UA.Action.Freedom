import type { RouteObject } from 'react-router-dom';

import { ConvoyCreatePage } from './ConvoyCreatePage';
import { ConvoyDetailPage } from './ConvoyDetailPage';
import { ConvoyEditPage } from './ConvoyEditPage';
import { ConvoysListPage } from './ConvoysListPage';

export const convoyRoutes: RouteObject[] = [
  { index: true, element: <ConvoysListPage /> },
  { path: 'new', element: <ConvoyCreatePage /> },
  { path: ':id', element: <ConvoyDetailPage /> },
  { path: ':id/edit', element: <ConvoyEditPage /> },
];
