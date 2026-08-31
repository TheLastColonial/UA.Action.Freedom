import { describe, expect, it } from 'vitest';

import { ALL_POLICIES, policySatisfiedBy } from './policyMatrix';
import type { Policy } from './policyMatrix';
import type { Role } from './roles';

// The authoritative matrix from docs/local-authentication.md § "Which role can do what".
// The API is the enforcement point; this table only drives what the UI offers.
const EXPECTED: Record<Policy, readonly Role[]> = {
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
};

const ALL_ROLES: readonly Role[] = [
  'Administrator',
  'Purchaser',
  'Dispatcher',
  'Loader',
  'GroundOfficer',
];

describe('policySatisfiedBy', () => {
  it('covers exactly the 15 documented policies', () => {
    expect([...ALL_POLICIES].sort()).toEqual(Object.keys(EXPECTED).sort());
  });

  for (const policy of Object.keys(EXPECTED) as Policy[]) {
    for (const role of ALL_ROLES) {
      const allowed = EXPECTED[policy].includes(role);
      it(`${role} ${allowed ? 'satisfies' : 'does not satisfy'} ${policy}`, () => {
        expect(policySatisfiedBy([role], policy)).toBe(allowed);
      });
    }
  }

  it('is false when the holder has no roles', () => {
    expect(policySatisfiedBy([], 'vehicles:read')).toBe(false);
  });

  it('is true when any held role satisfies the policy', () => {
    expect(policySatisfiedBy(['GroundOfficer', 'Loader'], 'boxes:validate')).toBe(true);
  });
});
