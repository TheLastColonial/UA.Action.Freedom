import type { RouteObject } from 'react-router-dom';

import { ConvoyChecklistPage } from './ConvoyChecklistPage';
import { ConvoyCreatePage } from './ConvoyCreatePage';
import { ConvoyDetailPage } from './ConvoyDetailPage';
import { ConvoyEditPage } from './ConvoyEditPage';
import { ConvoyReportPage } from './ConvoyReportPage';
import { ConvoysListPage } from './ConvoysListPage';
import { ManifestCreatePage } from '../manifests/ManifestCreatePage';

export const convoyRoutes: RouteObject[] = [
  { index: true, element: <ConvoysListPage /> },
  { path: 'new', element: <ConvoyCreatePage /> },
  { path: ':id', element: <ConvoyDetailPage /> },
  { path: ':id/report', element: <ConvoyReportPage /> },
  // The Convoy Leader's checklist: a phone page that stores nothing (ADR 0016).
  { path: ':id/checklist', element: <ConvoyChecklistPage /> },
  { path: ':id/edit', element: <ConvoyEditPage /> },
  // A manifest is opened against a truck-list entry, so its creation lives under the convoy —
  // the same shape as POST /convoys/{id}/vehicles/{vin}/manifest.
  { path: ':convoyId/vehicles/:vin/manifest/new', element: <ManifestCreatePage /> },
];
