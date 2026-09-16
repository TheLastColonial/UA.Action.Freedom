import type { RouteObject } from 'react-router-dom';

import { VehicleCreatePage } from './VehicleCreatePage';
import { VehicleDetailPage } from './VehicleDetailPage';
import { VehicleEditPage } from './VehicleEditPage';
import { VehicleServicingPage } from './VehicleServicingPage';
import { VehiclesListPage } from './VehiclesListPage';

export const vehicleRoutes: RouteObject[] = [
  { index: true, element: <VehiclesListPage /> },
  { path: 'new', element: <VehicleCreatePage /> },
  { path: ':vin', element: <VehicleDetailPage /> },
  { path: ':vin/edit', element: <VehicleEditPage /> },
  { path: ':vin/servicing', element: <VehicleServicingPage /> },
];
