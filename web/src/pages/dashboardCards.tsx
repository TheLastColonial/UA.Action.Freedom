import type { JSX } from 'react';

import type { Policy } from '../auth/policyMatrix';
import {
  BoxIcon,
  DocumentIcon,
  MapPinIcon,
  PeopleIcon,
  RouteIcon,
  VehicleIcon,
  WarehouseIcon,
} from './dashboardIcons';

export interface DashboardCardEntry {
  readonly label: string;
  readonly to: string;
  readonly policy: Policy;
  readonly icon: (props: { className?: string }) => JSX.Element;
}

// Cards are navigation only. Creating something happens on its own section's list page, which
// carries the "New ..." button behind the same write policy.
export const DASHBOARD_CARD_ENTRIES: readonly DashboardCardEntry[] = [
  {
    label: 'Vehicles',
    to: '/vehicles',
    policy: 'vehicles:read',
    icon: VehicleIcon,
  },
  {
    label: 'Volunteers',
    to: '/people',
    policy: 'people:read',
    icon: PeopleIcon,
  },
  {
    label: 'Convoys',
    to: '/convoys',
    policy: 'convoys:read',
    icon: RouteIcon,
  },
  {
    label: 'Boxes',
    to: '/boxes',
    policy: 'boxes:read',
    icon: BoxIcon,
  },
  {
    label: 'Manifests',
    to: '/manifests',
    policy: 'manifests:read',
    icon: DocumentIcon,
  },
  {
    label: 'Receivers',
    to: '/receivers',
    policy: 'receivers:read',
    icon: MapPinIcon,
  },
  {
    label: 'Locations',
    to: '/locations',
    policy: 'locations:read',
    icon: WarehouseIcon,
  },
];
