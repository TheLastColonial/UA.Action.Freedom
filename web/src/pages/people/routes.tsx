import type { RouteObject } from 'react-router-dom';

import { PeopleListPage } from './PeopleListPage';
import { PersonCreatePage } from './PersonCreatePage';
import { PersonDetailPage } from './PersonDetailPage';
import { PersonEditPage } from './PersonEditPage';

export const peopleRoutes: RouteObject[] = [
  { index: true, element: <PeopleListPage /> },
  { path: 'new', element: <PersonCreatePage /> },
  { path: ':id', element: <PersonDetailPage /> },
  { path: ':id/edit', element: <PersonEditPage /> },
];
