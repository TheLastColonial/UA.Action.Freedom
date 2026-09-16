import type { JSX } from 'react';

import type { Policy } from '../auth/policyMatrix';
import {
  BoxIcon,
  DocumentIcon,
  MapPinIcon,
  PeopleIcon,
  RouteIcon,
  VehicleIcon,
} from './dashboardIcons';

export interface DashboardCardEntry {
  readonly label: string;
  readonly to: string;
  readonly policy: Policy;
  readonly icon: (props: { className?: string }) => JSX.Element;
  readonly actionLabel: string;
  readonly actionTo: string;
  readonly actionPolicy: Policy;
}

export const DASHBOARD_CARD_ENTRIES: readonly DashboardCardEntry[] = [
  {
    label: 'Vehicles',
    to: '/vehicles',
    policy: 'vehicles:read',
    icon: VehicleIcon,
    actionLabel: 'New Vehicle',
    actionTo: '/vehicles/new',
    actionPolicy: 'vehicles:write',
  },
  {
    label: 'Volunteers',
    to: '/people',
    policy: 'people:read',
    icon: PeopleIcon,
    actionLabel: 'New Volunteer',
    actionTo: '/people/new',
    actionPolicy: 'people:write',
  },
  {
    label: 'Convoys',
    to: '/convoys',
    policy: 'convoys:read',
    icon: RouteIcon,
    actionLabel: 'New Convoy',
    actionTo: '/convoys/new',
    actionPolicy: 'convoys:write',
  },
  {
    label: 'Boxes',
    to: '/boxes',
    policy: 'boxes:read',
    icon: BoxIcon,
    actionLabel: 'New Box',
    actionTo: '/boxes/new',
    actionPolicy: 'boxes:write',
  },
  {
    label: 'Manifests',
    to: '/manifests',
    policy: 'manifests:read',
    icon: DocumentIcon,
    actionLabel: 'New Manifest',
    actionTo: '/manifests/new',
    actionPolicy: 'manifests:write',
  },
  {
    label: 'Receivers',
    to: '/receivers',
    policy: 'receivers:read',
    icon: MapPinIcon,
    actionLabel: 'New Receiver',
    actionTo: '/receivers/new',
    actionPolicy: 'receivers:write',
  },
];
