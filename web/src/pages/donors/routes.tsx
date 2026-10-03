import type { RouteObject } from 'react-router-dom';

import { DonationCreatePage } from './DonationCreatePage';
import { DonorCreatePage } from './DonorCreatePage';
import { DonorDetailPage } from './DonorDetailPage';
import { DonorEditPage } from './DonorEditPage';
import { DonorReportPage } from './DonorReportPage';
import { DonorsListPage } from './DonorsListPage';

export const donorRoutes: RouteObject[] = [
  { index: true, element: <DonorsListPage /> },
  { path: 'new', element: <DonorCreatePage /> },
  { path: ':id', element: <DonorDetailPage /> },
  { path: ':id/edit', element: <DonorEditPage /> },
  { path: ':id/donations/new', element: <DonationCreatePage /> },
  { path: ':id/report', element: <DonorReportPage /> },
];
