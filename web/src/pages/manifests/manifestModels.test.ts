import { describe, expect, it } from 'vitest';

import {
  emptyManifestForm,
  manifestFormSchema,
  manifestFormToRequest,
  manifestFormToUpdateRequest,
  manifestToFormValues,
} from './manifestModels';
import { makeManifest } from '../../test/factories/manifest';

describe('manifestFormToRequest', () => {
  it('trims the id and omits every empty optional', () => {
    const request = manifestFormToRequest({
      ...emptyManifestForm(),
      id: '  UA-2026-07 ',
      ferryBookingComplete: true,
    });
    expect(request).toEqual({ id: 'UA-2026-07', ferryBookingComplete: true });
  });

  it('keeps the notes when set', () => {
    const request = manifestFormToRequest({
      id: 'M1',
      deliveryNotes: 'Fragile',
      ferryBookingComplete: false,
    });
    expect(request).toEqual({ id: 'M1', deliveryNotes: 'Fragile', ferryBookingComplete: false });
  });

  it('carries no convoy or vehicle: the route does', () => {
    // A manifest is opened at POST /convoys/{id}/vehicles/{vin}/manifest, against the truck-list
    // entry it is the paperwork for. There is no field here to point one at a truck that is on a
    // different convoy, or none.
    const request = manifestFormToRequest({ ...emptyManifestForm(), id: 'M1' });

    expect('vin' in request).toBe(false);
    expect('convoyId' in request).toBe(false);
  });

  it('update request drops the id', () => {
    const request = manifestFormToUpdateRequest({
      ...emptyManifestForm(),
      id: 'M1',
      ferryBookingComplete: false,
    });
    expect('id' in request).toBe(false);
  });

  it('an edit cannot reach the convoy or the vehicle either', () => {
    // They are the manifest's identity, and the UPDATE never names those columns.
    const request = manifestFormToUpdateRequest({ ...emptyManifestForm(), id: 'M1' });

    expect('vin' in request).toBe(false);
    expect('convoyId' in request).toBe(false);
  });
});

describe('manifestToFormValues', () => {
  it('offers only what an edit may change', () => {
    const values = manifestToFormValues(
      makeManifest({ id: 'M1', convoyId: 7, vin: 'VIN-1', deliveryNotes: 'Fragile' }),
    );

    expect(values).toEqual({ id: 'M1', deliveryNotes: 'Fragile', ferryBookingComplete: false });
  });
});

describe('manifestFormSchema', () => {
  it('requires a reference', () => {
    const result = manifestFormSchema.safeParse({ ...emptyManifestForm(), id: '' });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((issue) => issue.message)).toContain(
      'A manifest reference is required',
    );
  });
});
