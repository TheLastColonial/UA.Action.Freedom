import type { RouteObject } from 'react-router-dom';

import { CategoriesListPage } from './CategoriesListPage';
import { CategoryCreatePage } from './CategoryCreatePage';
import { CategoryEditPage } from './CategoryEditPage';

export const categoryRoutes: RouteObject[] = [
  { index: true, element: <CategoriesListPage /> },
  { path: 'new', element: <CategoryCreatePage /> },
  { path: ':id/edit', element: <CategoryEditPage /> },
];
