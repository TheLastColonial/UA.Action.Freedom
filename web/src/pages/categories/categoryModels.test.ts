import { describe, expect, it } from 'vitest';

import { makeCategory } from '../../test/factories/category';
import {
  categoryFormSchema,
  categoryFormToRequest,
  categoryToFormValues,
  codeFormSchema,
  codeOf,
  codeToRequestValue,
  emptyCategoryForm,
} from './categoryModels';

describe('categoryFormToRequest', () => {
  it('omits every empty optional field', () => {
    expect(categoryFormToRequest({ ...emptyCategoryForm(), nameEn: ' Bedding ' })).toEqual({
      nameEn: 'Bedding',
      isSensitive: false,
      isNotCarried: false,
    });
  });

  it('coerces the hazard class and the shelf-life days', () => {
    expect(
      categoryFormToRequest({
        ...emptyCategoryForm(),
        nameEn: 'Gas',
        nameUk: ' Газ ',
        hazardClass: '2',
        isNotCarried: true,
        warnWithinDays: '90',
      }),
    ).toEqual({
      nameEn: 'Gas',
      nameUk: 'Газ',
      hazardClass: 2,
      isSensitive: false,
      isNotCarried: true,
      warnWithinDays: 90,
    });
  });
});

describe('categoryToFormValues', () => {
  it('renders nulls as empty strings', () => {
    expect(categoryToFormValues(makeCategory({ nameEn: 'Food' }))).toEqual({
      ...emptyCategoryForm(),
      nameEn: 'Food',
    });
  });
});

describe('categoryFormSchema', () => {
  it('requires a name', () => {
    const result = categoryFormSchema.safeParse({ ...emptyCategoryForm(), nameEn: '  ' });
    expect(result.error?.issues.map((i) => i.message)).toContain('Name is required');
  });

  it.each(['0', '10', 'x'])('rejects a hazard class of %s', (hazardClass) => {
    expect(
      categoryFormSchema.safeParse({ ...emptyCategoryForm(), nameEn: 'Gas', hazardClass }).success,
    ).toBe(false);
  });

  it.each(['-1', '1.5', 'soon'])('rejects shelf-life days of %s', (warnWithinDays) => {
    expect(
      categoryFormSchema.safeParse({ ...emptyCategoryForm(), nameEn: 'Food', warnWithinDays })
        .success,
    ).toBe(false);
  });
});

describe('customs codes', () => {
  it.each(['', '300490', '3004900000'])('accepts %j', (code) => {
    expect(codeFormSchema.safeParse({ code }).success).toBe(true);
  });

  it.each(['12345', '12345678901', '30049A'])('rejects %j', (code) => {
    expect(codeFormSchema.safeParse({ code }).success).toBe(false);
  });

  it('sends a blank code as null so the mapping is cleared', () => {
    expect(codeToRequestValue('  ')).toBeNull();
    expect(codeToRequestValue(' 300490 ')).toBe('300490');
  });

  it('reads the code for an authority', () => {
    const category = makeCategory({ ukCode: '111111', euCode: null, uaCode: '3333333333' });

    expect(codeOf(category, 'UK')).toBe('111111');
    expect(codeOf(category, 'EU')).toBe('');
    expect(codeOf(category, 'UA')).toBe('3333333333');
  });
});
