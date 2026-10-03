import { describe, expect, it } from 'vitest';

import {
  addItemFormSchema,
  addItemFormToRequest,
  boxFormToRequest,
  emptyAddItemForm,
  validateFormSchema,
  validateFormToRequest,
} from './boxModels';

describe('boxFormToRequest', () => {
  it('omits every empty optional field', () => {
    expect(boxFormToRequest({ receiverRef: '', locationId: '' })).toEqual({});
  });

  it('trims and keeps the fields that are set', () => {
    const request = boxFormToRequest({ receiverRef: ' abc ', locationId: '3' });
    expect(request).toEqual({ receiverRef: 'abc', locationId: 3 });
  });
});

const filledItem = {
  ...emptyAddItemForm(),
  description: '  Blankets ',
  categoryId: '4',
};

describe('addItemFormToRequest', () => {
  it('folds property rows into an object and drops rows with a blank key', () => {
    const request = addItemFormToRequest({
      ...filledItem,
      properties: [
        { key: 'size', value: 'large' },
        { key: '  ', value: 'ignored' },
      ],
    });
    expect(request.description).toBe('Blankets');
    expect(request.properties).toEqual({ size: 'large' });
  });

  it('sends the category as a number and omits every empty optional field', () => {
    const request = addItemFormToRequest(filledItem);

    expect(request).toEqual({ description: 'Blankets', properties: {}, categoryId: 4 });
  });

  it('coerces the quantity and value and keeps the expiry and the code', () => {
    const request = addItemFormToRequest({
      ...filledItem,
      quantity: ' 40 ',
      valueGbp: '62.50',
      valueSource: 'Estimate',
      expiresOn: '2027-03-31',
      commodityCode: ' 30049000 ',
    });

    expect(request).toMatchObject({
      quantity: 40,
      valueGbp: 62.5,
      valueSource: 'Estimate',
      expiresOn: '2027-03-31',
      commodityCode: '30049000',
    });
  });
});

describe('addItemFormSchema', () => {
  it('requires a description', () => {
    const result = addItemFormSchema.safeParse({ ...filledItem, description: '   ' });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain('Describe the item');
  });

  it('requires a category', () => {
    const result = addItemFormSchema.safeParse({ ...filledItem, categoryId: '' });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain('Choose a category');
  });

  it('rejects more than 50 properties', () => {
    const properties = Array.from({ length: 51 }, (_v, i) => ({
      key: `k${String(i)}`,
      value: 'v',
    }));
    const result = addItemFormSchema.safeParse({ ...filledItem, properties });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'An item may carry at most 50 properties',
    );
  });

  it('asks for the source of a value, and the value of a source', () => {
    const noSource = addItemFormSchema.safeParse({ ...filledItem, valueGbp: '10' });
    expect(noSource.error?.issues.map((i) => i.path.join('.'))).toContain('valueSource');

    const noValue = addItemFormSchema.safeParse({ ...filledItem, valueSource: 'Donor' });
    expect(noValue.error?.issues.map((i) => i.path.join('.'))).toContain('valueGbp');
  });

  it.each(['0', '1.5', 'abc', '-3'])('rejects a quantity of %s', (quantity) => {
    expect(addItemFormSchema.safeParse({ ...filledItem, quantity }).success).toBe(false);
  });

  it.each(['10.555', '-1', 'ten'])('rejects a value of %s', (valueGbp) => {
    const result = addItemFormSchema.safeParse({
      ...filledItem,
      valueGbp,
      valueSource: 'Donor',
    });
    expect(result.success).toBe(false);
  });

  it.each(['1234', '12345678901', '30049A'])('rejects a commodity code of %s', (commodityCode) => {
    expect(addItemFormSchema.safeParse({ ...filledItem, commodityCode }).success).toBe(false);
  });

  it('accepts a complete item', () => {
    const result = addItemFormSchema.safeParse({
      ...filledItem,
      quantity: '40',
      valueGbp: '62.5',
      valueSource: 'Donor',
      expiresOn: '2027-03-31',
      commodityCode: '30049000',
    });
    expect(result.success).toBe(true);
  });
});

describe('validate a box', () => {
  it('coerces the weight to a number', () => {
    expect(
      validateFormToRequest({
        weightKg: '12',
        widthCm: '',
        depthCm: '',
        heightCm: '',
      }),
    ).toEqual({
      weightKg: 12,
    });
  });

  it('never names who is validating: the API signs as the caller', () => {
    const request = validateFormToRequest({
      weightKg: '12',
      widthCm: '',
      depthCm: '',
      heightCm: '',
    });
    expect(Object.keys(request)).not.toContain('validatedByPersonId');
  });

  it('coerces the dimensions when given, and omits them when blank', () => {
    const request = validateFormToRequest({
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

  it('needs a weight in 1..500', () => {
    expect(validateFormSchema.safeParse({ weightKg: '', ...blankDimensions }).success).toBe(false);
    expect(validateFormSchema.safeParse({ weightKg: '0', ...blankDimensions }).success).toBe(false);
    expect(
      validateFormSchema.safeParse({
        weightKg: '501',
        ...blankDimensions,
      }).success,
    ).toBe(false);
    expect(
      validateFormSchema.safeParse({
        weightKg: '250',
        ...blankDimensions,
      }).success,
    ).toBe(true);
  });

  it('leaves dimensions optional but bounded to 1..1000 with up to 2 decimal places', () => {
    const base = { weightKg: '250', ...blankDimensions };
    expect(validateFormSchema.safeParse({ ...base, widthCm: '' }).success).toBe(true);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '40.25' }).success).toBe(true);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '40.256' }).success).toBe(false);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '0' }).success).toBe(false);
    expect(validateFormSchema.safeParse({ ...base, widthCm: '1001' }).success).toBe(false);
  });
});
