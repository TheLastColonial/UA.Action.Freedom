import { describe, expect, it } from 'vitest';

import {
  addItemFormSchema,
  addItemFormToRequest,
  boxFormToRequest,
  validateFormSchema,
  validateFormToRequest,
} from './boxModels';

describe('boxFormToRequest', () => {
  it('omits every empty optional field', () => {
    expect(
      boxFormToRequest({
        receiverRef: '',
        house: '',
        street: '',
        city: '',
        country: '',
        postcode: '',
      }),
    ).toEqual({});
  });

  it('trims and keeps the fields that are set', () => {
    const request = boxFormToRequest({
      receiverRef: ' abc ',
      house: '1',
      street: '',
      city: 'Lviv',
      country: '',
      postcode: '',
    });
    expect(request).toEqual({ receiverRef: 'abc', house: '1', city: 'Lviv' });
  });
});

describe('addItemFormToRequest', () => {
  it('folds property rows into an object and drops rows with a blank key', () => {
    const request = addItemFormToRequest({
      description: '  Blankets ',
      properties: [
        { key: 'size', value: 'large' },
        { key: '  ', value: 'ignored' },
      ],
    });
    expect(request.description).toBe('Blankets');
    expect(request.properties).toEqual({ size: 'large' });
  });
});

describe('addItemFormSchema', () => {
  it('requires a description', () => {
    const result = addItemFormSchema.safeParse({ description: '   ', properties: [] });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain('Describe the item');
  });

  it('rejects more than 50 properties', () => {
    const properties = Array.from({ length: 51 }, (_v, i) => ({
      key: `k${String(i)}`,
      value: 'v',
    }));
    const result = addItemFormSchema.safeParse({ description: 'x', properties });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'An item may carry at most 50 properties',
    );
  });
});

describe('validate a box', () => {
  it('coerces the weight to a number', () => {
    expect(validateFormToRequest({ validatedByPersonId: 'p1', weightKg: '12' })).toEqual({
      validatedByPersonId: 'p1',
      weightKg: 12,
    });
  });

  it('needs a volunteer and a weight in 1..500', () => {
    expect(validateFormSchema.safeParse({ validatedByPersonId: '', weightKg: '12' }).success).toBe(
      false,
    );
    expect(validateFormSchema.safeParse({ validatedByPersonId: 'p1', weightKg: '0' }).success).toBe(
      false,
    );
    expect(
      validateFormSchema.safeParse({ validatedByPersonId: 'p1', weightKg: '501' }).success,
    ).toBe(false);
    expect(
      validateFormSchema.safeParse({ validatedByPersonId: 'p1', weightKg: '250' }).success,
    ).toBe(true);
  });
});
