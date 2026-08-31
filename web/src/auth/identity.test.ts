import { describe, expect, it } from 'vitest';

import { deriveIdentity } from './identity';

describe('deriveIdentity', () => {
  it('reads sub and the roles claim', () => {
    const identity = deriveIdentity({ sub: 'abc-123', roles: ['Dispatcher', 'Loader'] });

    expect(identity.sub).toBe('abc-123');
    expect(identity.roles).toEqual(['Dispatcher', 'Loader']);
  });

  it('answers hasRole and hasPolicy from the roles held', () => {
    const identity = deriveIdentity({ sub: 'x', roles: ['GroundOfficer'] });

    expect(identity.hasRole('GroundOfficer')).toBe(true);
    expect(identity.hasRole('Administrator')).toBe(false);
    expect(identity.hasPolicy('receivers:detail')).toBe(true);
    expect(identity.hasPolicy('vehicles:read')).toBe(false);
  });

  it('is a well-formed anonymous identity when there is no profile', () => {
    const identity = deriveIdentity(undefined);

    expect(identity.sub).toBeNull();
    expect(identity.roles).toEqual([]);
    expect(identity.hasPolicy('vehicles:read')).toBe(false);
  });

  it('treats a non-string sub as absent', () => {
    expect(deriveIdentity({ sub: 42, roles: [] }).sub).toBeNull();
  });
});
