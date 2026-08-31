import { describe, expect, it } from 'vitest';

import { ALL_ROLES, parseRolesClaim } from './roles';

describe('parseRolesClaim', () => {
  it('reads an array of role names', () => {
    expect(parseRolesClaim(['Administrator', 'Dispatcher'])).toEqual([
      'Administrator',
      'Dispatcher',
    ]);
  });

  it('normalises a single role name delivered as a bare string', () => {
    expect(parseRolesClaim('GroundOfficer')).toEqual(['GroundOfficer']);
  });

  it('drops values that are not known roles', () => {
    expect(parseRolesClaim(['Administrator', 'root', 'superuser'])).toEqual(['Administrator']);
  });

  it('is empty for a missing claim', () => {
    expect(parseRolesClaim(undefined)).toEqual([]);
    expect(parseRolesClaim(null)).toEqual([]);
  });

  it('is empty for a malformed claim rather than throwing', () => {
    expect(parseRolesClaim({ role: 'Administrator' })).toEqual([]);
    expect(parseRolesClaim(42)).toEqual([]);
  });

  it('exposes every application role', () => {
    expect([...ALL_ROLES].sort()).toEqual(
      ['Administrator', 'Dispatcher', 'GroundOfficer', 'Loader', 'Purchaser'].sort(),
    );
  });
});
