import { policySatisfiedBy } from './policyMatrix';
import type { Policy } from './policyMatrix';
import { parseRolesClaim } from './roles';
import type { Role } from './roles';

export interface Identity {
  /** The token `sub` — the principal id the receiver access log records. */
  readonly sub: string | null;
  readonly roles: readonly Role[];
  readonly hasRole: (role: Role) => boolean;
  readonly hasPolicy: (policy: Policy) => boolean;
}

interface TokenProfile {
  sub?: unknown;
  roles?: unknown;
}

export function deriveIdentity(profile: TokenProfile | null | undefined): Identity {
  const roles = parseRolesClaim(profile?.roles);
  const sub = typeof profile?.sub === 'string' ? profile.sub : null;

  return {
    sub,
    roles,
    hasRole: (role) => roles.includes(role),
    hasPolicy: (policy) => policySatisfiedBy(roles, policy),
  };
}
