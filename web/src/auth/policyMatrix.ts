import type { Role } from './roles';

// The authorization matrix, mirrored from docs/local-authentication.md § "Which role can do
// what". This drives what the UI offers — nav links, action buttons, the receiver-detail
// block. The API enforces the same policies independently; this table is never the guard.
export const POLICY_MATRIX = {
  'vehicles:read': ['Administrator', 'Purchaser', 'Dispatcher', 'Loader'],
  'vehicles:write': ['Administrator', 'Purchaser'],
  'people:read': ['Administrator', 'Purchaser', 'Dispatcher', 'Loader'],
  'people:write': ['Administrator'],
  'convoys:read': ['Administrator', 'Purchaser', 'Dispatcher', 'Loader'],
  'convoys:write': ['Administrator', 'Dispatcher'],
  'receivers:read': ['Administrator', 'Purchaser', 'Dispatcher', 'Loader', 'GroundOfficer'],
  'receivers:write': ['Administrator', 'GroundOfficer'],
  'receivers:detail': ['GroundOfficer'],
  'boxes:read': ['Administrator', 'Purchaser', 'Dispatcher', 'Loader'],
  'boxes:write': ['Administrator', 'Dispatcher', 'Loader'],
  'boxes:validate': ['Administrator', 'Loader'],
  'manifests:read': ['Administrator', 'Purchaser', 'Dispatcher', 'Loader'],
  'manifests:write': ['Administrator', 'Dispatcher'],
  'manifests:approve': ['Administrator'],
} as const satisfies Record<string, readonly Role[]>;

export type Policy = keyof typeof POLICY_MATRIX;

export const ALL_POLICIES = Object.keys(POLICY_MATRIX) as readonly Policy[];

export function policySatisfiedBy(roles: readonly Role[], policy: Policy): boolean {
  const permitted: readonly Role[] = POLICY_MATRIX[policy];
  return roles.some((role) => permitted.includes(role));
}
