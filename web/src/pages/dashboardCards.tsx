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
  /**
   * The card's "create one" shortcut, when the thing can be created from nothing. Manifests
   * cannot: one is opened against a vehicle on a convoy's truck list.
   */
  readonly actionLabel?: string;
  readonly actionTo?: string;
  readonly actionPolicy?: Policy;
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
    // No action: a manifest is opened against a truck-list entry, from its convoy, so there is
    // nowhere to send somebody who has not picked a vehicle yet.
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
    actionLabel: 'New Receiver',
    actionTo: '/receivers/new',
    actionPolicy: 'receivers:write',
  },
  {
    label: 'Locations',
    to: '/locations',
    policy: 'locations:read',
    icon: WarehouseIcon,
    actionLabel: 'New Location',
    actionTo: '/locations/new',
    actionPolicy: 'locations:write',
  },
];
