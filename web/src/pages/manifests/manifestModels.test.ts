import { describe, expect, it } from 'vitest';

import {
  emptyManifestForm,
  manifestFormSchema,
  manifestFormToRequest,
  manifestFormToUpdateRequest,
  teamFormSchema,
  teamFormToRequest,
} from './manifestModels';

describe('manifestFormToRequest', () => {
  it('trims the id and omits every empty optional', () => {
    const request = manifestFormToRequest({
      ...emptyManifestForm(),
      id: '  UA-2026-07 ',
      ferryBookingComplete: true,
    });
    expect(request).toEqual({ id: 'UA-2026-07', ferryBookingComplete: true });
  });

  it('coerces convoyId and keeps vin / notes when set', () => {
    const request = manifestFormToRequest({
      id: 'M1',
      vin: 'VIN123',
      convoyId: '42',
      deliveryNotes: 'Fragile',
      ferryBookingComplete: false,
    });
    expect(request).toEqual({
      id: 'M1',
      vin: 'VIN123',
      convoyId: 42,
      deliveryNotes: 'Fragile',
      ferryBookingComplete: false,
    });
  });

  it('update request drops the id', () => {
    const request = manifestFormToUpdateRequest({
      ...emptyManifestForm(),
      id: 'M1',
      ferryBookingComplete: false,
    });
    expect('id' in request).toBe(false);
  });
});

describe('manifestFormSchema', () => {
  it('requires a reference', () => {
    const result = manifestFormSchema.safeParse({ ...emptyManifestForm(), id: '' });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'A manifest reference is required',
    );
  });
});

describe('driver team', () => {
  it('omits an empty secondary driver', () => {
    expect(teamFormToRequest({ primaryPersonId: 'p1', secondaryPersonId: '  ' })).toEqual({
      primaryPersonId: 'p1',
    });
  });

  it('needs a primary driver', () => {
    const result = teamFormSchema.safeParse({ primaryPersonId: '', secondaryPersonId: '' });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'Name the volunteer leading this leg',
    );
  });

  it('rejects the same volunteer on both seats', () => {
    const result = teamFormSchema.safeParse({ primaryPersonId: 'p1', secondaryPersonId: 'p1' });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.path).toEqual(['secondaryPersonId']);
  });
});
