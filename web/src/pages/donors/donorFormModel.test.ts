import { describe, expect, it } from 'vitest';

import {
  donationFormSchema,
  donationFormToRequest,
  donorFormSchema,
  donorFormToRequest,
} from './donorFormModel';

describe('donorFormToRequest', () => {
  it('trims the name and leaves out contact details that were left blank', () => {
    expect(donorFormToRequest({ name: '  Margaret Hollis ', email: ' ', phone: '' })).toEqual({
      name: 'Margaret Hollis',
    });
  });

  it('keeps an email and a phone number when given', () => {
    expect(
      donorFormToRequest({ name: 'M', email: 'm@example.org', phone: '07700 900456' }),
    ).toEqual({ name: 'M', email: 'm@example.org', phone: '07700 900456' });
  });
});

describe('donorFormSchema', () => {
  it('requires a name', () => {
    expect(donorFormSchema.safeParse({ name: ' ', email: '', phone: '' }).success).toBe(false);
  });

  it('rejects an email that is not an address', () => {
    expect(donorFormSchema.safeParse({ name: 'M', email: 'nope', phone: '' }).success).toBe(false);
  });

  it('accepts a donor with no contact details', () => {
    expect(donorFormSchema.safeParse({ name: 'M', email: '', phone: '' }).success).toBe(true);
  });
});

describe('donation form', () => {
  it('names the donor and drops empty notes', () => {
    expect(donationFormToRequest('d1', { receivedOn: '2026-09-20', notes: ' ' })).toEqual({
      donorId: 'd1',
      receivedOn: '2026-09-20',
    });
  });

  it('requires a received date', () => {
    expect(donationFormSchema.safeParse({ receivedOn: '', notes: '' }).success).toBe(false);
  });
});
