import type { JSX, ReactNode } from 'react';

import { useAuth } from '../auth/useAuth';
import type { Policy } from '../auth/policyMatrix';
import type { Role } from '../auth/roles';

interface GateProps {
  policy?: Policy;
  role?: Role;
  children: ReactNode;
  fallback?: ReactNode;
}

/**
 * Renders `children` only when the signed-in user carries the given policy and/or role.
 * This shapes what the UI offers; the API enforces the same rules independently.
 */
export function Gate({ policy, role, children, fallback = null }: GateProps): JSX.Element {
  const auth = useAuth();
  const allowed =
    (policy === undefined || auth.hasPolicy(policy)) && (role === undefined || auth.hasRole(role));

  return <>{allowed ? children : fallback}</>;
}
