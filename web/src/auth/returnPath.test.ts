import { describe, expect, it } from 'vitest';

import { returnPathFrom } from './returnPath';

describe('returnPathFrom', () => {
  it('returns to the in-app page the user was on before signing in', () => {
    expect(returnPathFrom({ returnTo: '/vehicles/VIN-1/servicing?tab=notes' })).toBe(
      '/vehicles/VIN-1/servicing?tab=notes',
    );
  });

  it.each([
    ['no state', undefined],
    ['a state of the wrong shape', 'vehicles'],
    ['an empty path', { returnTo: '' }],
    ['a relative path', { returnTo: 'vehicles' }],
    ['a protocol-relative URL', { returnTo: '//evil.example/app' }],
    ['an absolute URL', { returnTo: 'https://evil.example/app' }],
    ['a backslash trick', { returnTo: '/\\evil.example' }],
  ])('falls back to the dashboard for %s', (_case, state) => {
    expect(returnPathFrom(state)).toBe('/');
  });
});
