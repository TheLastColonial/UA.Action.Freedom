import { describe, expect, it } from 'vitest';

import {
  addItemFormSchema,
  addItemFormToRequest,
  boxFormSchema,
  boxFormToRequest,
  emptyBoxForm,
  validateFormSchema,
  validateFormToRequest,
} from './boxModels';

describe('boxFormToRequest', () => {
  it('omits every empty optional field', () => {
    expect(
      boxFormToRequest({ receiverRef: '', locationId: '', bayId: '', assignedByPersonId: '' }),
    ).toEqual({});
  });

  it('trims and keeps the fields that are set', () => {
    const request = boxFormToRequest({
      receiverRef: ' abc ',
      locationId: '3',
      bayId: '',
      assignedByPersonId: '',
    });
    expect(request).toEqual({ receiverRef: 'abc', locationId: 3 });
  });

  it('never carries the create-time bay fields onto the box request', () => {
    const request = boxFormToRequest({
      receiverRef: '',
      locationId: '3',
      bayId: '9',
      assignedByPersonId: 'p1',
    });
    expect(request).toEqual({ locationId: 3 });
  });
});

describe('boxFormSchema', () => {
  it('starts with both bay fields blank', () => {
    expect(emptyBoxForm()).toEqual({
      receiverRef: '',
      locationId: '',
      bayId: '',
      assignedByPersonId: '',
    });
  });

  it('allows no bay to be chosen at all', () => {
    const result = boxFormSchema.safeParse({
      receiverRef: '',
      locationId: '',
      bayId: '',
      assignedByPersonId: '',
    });
    expect(result.success).toBe(true);
  });

  it('requires the assigner once a bay is chosen', () => {
    const result = boxFormSchema.safeParse({
      receiverRef: '',
      locationId: '3',
      bayId: '9',
      assignedByPersonId: '',
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'Name the volunteer placing the box',
    );
  });

  it('accepts a bay named alongside its assigner', () => {
    const result = boxFormSchema.safeParse({
      receiverRef: '',
      locationId: '3',
      bayId: '9',
      assignedByPersonId: 'p1',
    });
    expect(result.success).toBe(true);
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
    expect(
      validateFormToRequest({
        validatedByPersonId: 'p1',
        weightKg: '12',
        widthCm: '',
        depthCm: '',
        heightCm: '',
      }),
    ).toEqual({
      validatedByPersonId: 'p1',
      weightKg: 12,
    });
  });

  it('coerces the dimensions when given, and omits them when blank', () => {
    const request = validateFormToRequest({
      validatedByPersonId: 'p1',
      weightKg: '12',
      widthCm: '40',
      depthCm: '30.5',
      heightCm: '',
    });
    expect(request.widthCm).toBe(40);
    expect(request.depthCm).toBe(30.5);
    expect('heightCm' in request).toBe(false);
  });

  const blankDimensions = { widthCm: '', depthCm: '', heightCm: '' };

  it('needs a volunteer and a weight in 1..500', () => {
    expect(
      validateFormSchema.safeParse({ validatedByPersonId: '', weightKg: '12', ...blankDimensions })
        .success,
    ).toBe(false);
    expect(
      validateFormSchema.safeParse({ validatedByPersonId: 'p1', weightKg: '0', ...blankDimensions })
        .success,
    ).toBe(false);
    expect(
      validateFormSchema.safeParse({
        validatedByPersonId: 'p1',
        weightKg: '501',
        ...blankDimensions,
      }).success,
    ).toBe(false);
    expect(
      validateFormSchema.safeParse({
        validatedByPersonId: 'p1',
        weightKg: '250',
        ...blankDimensions,
      }).success,
    ).toBe(true);
  });

  it('leaves dimensions optional but bounded to 1..1000 with up to 2 decimal places', () => {
    const base = { validatedByPersonId: 'p1', weightKg: '250', ...blankDimensions };
    expect(validateFormSchema.safeParse({ ...base, widthCm: '' }).success).toBe(true);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '40.25' }).success).toBe(true);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '40.256' }).success).toBe(false);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '0' }).success).toBe(false);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '1001' }).success).toBe(false);
  });
});
