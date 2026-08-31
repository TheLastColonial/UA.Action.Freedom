import type { Policy } from '../auth/policyMatrix';

export interface NavEntry {
  readonly label: string;
  readonly to: string;
  /** The read policy that gates this destination. Absent = always shown. */
  readonly policy?: Policy;
}

export const NAV: readonly NavEntry[] = [
  { label: 'Dashboard', to: '/' },
  { label: 'Vehicles', to: '/vehicles', policy: 'vehicles:read' },
  { label: 'Volunteers', to: '/people', policy: 'people:read' },
  { label: 'Convoys', to: '/convoys', policy: 'convoys:read' },
  { label: 'Boxes', to: '/boxes', policy: 'boxes:read' },
  { label: 'Manifests', to: '/manifests', policy: 'manifests:read' },
  { label: 'Receivers', to: '/receivers', policy: 'receivers:read' },
];
