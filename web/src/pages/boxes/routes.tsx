import type { RouteObject } from 'react-router-dom';

import { BoxCreatePage } from './BoxCreatePage';
import { BoxDetailPage } from './BoxDetailPage';
import { BoxEditPage } from './BoxEditPage';
import { BoxesListPage } from './BoxesListPage';

export const boxRoutes: RouteObject[] = [
  { index: true, element: <BoxesListPage /> },
  { path: 'new', element: <BoxCreatePage /> },
  { path: ':id', element: <BoxDetailPage /> },
  { path: ':id/edit', element: <BoxEditPage /> },
];
