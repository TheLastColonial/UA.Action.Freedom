import { describe, expect, it } from 'vitest';

import {
  detailFormSchema,
  detailFormToRequest,
  emptyDetailForm,
  receiverFormSchema,
  receiverFormToRequest,
} from './receiverModels';

describe('receiverFormToRequest', () => {
  it('trims organisation and region', () => {
    expect(receiverFormToRequest({ organisation: '  Aid Co ', region: ' Kyiv ' })).toEqual({
      organisation: 'Aid Co',
      region: 'Kyiv',
    });
  });
});

describe('receiverFormSchema', () => {
  it('requires both fields', () => {
    const result = receiverFormSchema.safeParse({ organisation: '', region: '' });
    const messages = result.error?.issues.map((i) => i.message) ?? [];
    expect(messages).toContain('Organisation is required');
    expect(messages).toContain('Region is required');
  });
});

describe('detailFormToRequest', () => {
  it('trims required fields and omits empty optionals', () => {
    const request = detailFormToRequest({
      ...emptyDetailForm(),
      contactName: '  Iryna ',
      contactPhone: ' +380 44 000 0000 ',
      addressLine1: ' 12 Main St ',
      city: ' Lviv ',
    });
    expect(request).toEqual({
      contactName: 'Iryna',
      contactPhone: '+380 44 000 0000',
      addressLine1: '12 Main St',
      city: 'Lviv',
    });
    expect('addressLine2' in request).toBe(false);
    expect('postCode' in request).toBe(false);
  });
});

describe('detailFormSchema', () => {
  it('reports missing required fields with static messages that do not echo input', () => {
    const result = detailFormSchema.safeParse({ ...emptyDetailForm() });
    const messages = result.error?.issues.map((i) => i.message) ?? [];
    expect(messages).toContain('A contact name is required');
    expect(messages).toContain('The first address line is required');
    for (const message of messages) {
      expect(message).not.toMatch(/\d{2,}/);
    }
  });
});
