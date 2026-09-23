import type { Policy } from '../auth/policyMatrix';

export interface NavEntry {
  readonly label: string;
  readonly to: string;
  /** The read policy that gates this destination. Absent = always shown. */
  readonly policy?: Policy;
}

/** The top of the navigation tree. Always shown: every signed-in user has a dashboard. */
export const NAV_HOME: NavEntry = { label: 'Dashboard', to: '/' };

/** The sections, nested under the dashboard, each gated by its read policy. */
export const NAV_SECTIONS: readonly NavEntry[] = [
  { label: 'Vehicles', to: '/vehicles', policy: 'vehicles:read' },
  { label: 'Volunteers', to: '/people', policy: 'people:read' },
  { label: 'Convoys', to: '/convoys', policy: 'convoys:read' },
  { label: 'Boxes', to: '/boxes', policy: 'boxes:read' },
  { label: 'Manifests', to: '/manifests', policy: 'manifests:read' },
  { label: 'Receivers', to: '/receivers', policy: 'receivers:read' },
  { label: 'Locations', to: '/locations', policy: 'locations:read' },
];

/** Every destination, flat — the home first. */
export const NAV: readonly NavEntry[] = [NAV_HOME, ...NAV_SECTIONS];
