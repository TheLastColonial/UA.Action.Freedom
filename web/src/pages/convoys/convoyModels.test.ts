import { describe, expect, it } from 'vitest';

import { convoyFormSchema, convoyToFormValues } from './convoyFormModel';
import { routeFormSchema, routeStopsFormToRequest } from './routeModel';

describe('convoyFormSchema', () => {
  it('accepts an end after the start', () => {
    const result = convoyFormSchema.safeParse({
      start: '2026-03-01T08:00',
      expectedEnd: '2026-03-06T20:00',
    });
    expect(result.success).toBe(true);
  });

  it('rejects an end before the start, against the expectedEnd field', () => {
    const result = convoyFormSchema.safeParse({
      start: '2026-03-06T08:00',
      expectedEnd: '2026-03-01T20:00',
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues[0]?.path).toEqual(['expectedEnd']);
  });
});

describe('convoyToFormValues', () => {
  it('reduces the timestamps to what datetime-local wants', () => {
    const values = convoyToFormValues({
      id: 1,
      start: '2026-03-01T08:00:00',
      expectedEnd: '2026-03-06T20:00:00',
      truckListPublishedAt: null,
      truckListPublished: false,
    });
    expect(values.start).toBe('2026-03-01T08:00');
  });
});

describe('routeStopsFormToRequest', () => {
  it('trims, drops empty optionals and keeps list order', () => {
    const request = routeStopsFormToRequest([
      { house: ' 1 ', street: 'High St', city: '', country: 'UK', postcode: ' M1 1AA ' },
      { house: '', street: '', city: '', country: '', postcode: 'SW1A 1AA' },
    ]);

    const [first] = request.stops;
    expect(request.stops).toHaveLength(2);
    expect(first).toEqual({ postcode: 'M1 1AA', house: '1', street: 'High St', country: 'UK' });
    expect(first === undefined ? true : 'city' in first).toBe(false);
  });
});

describe('routeFormSchema', () => {
  it('requires a postcode on every stop', () => {
    const result = routeFormSchema.safeParse({
      stops: [{ house: '', street: '', city: '', country: '', postcode: '' }],
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain('Postcode is required');
  });

  it('rejects more than 100 stops', () => {
    const stops = Array.from({ length: 101 }, () => ({
      house: '',
      street: '',
      city: '',
      country: '',
      postcode: 'M1 1AA',
    }));
    const result = routeFormSchema.safeParse({ stops });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.message)).toContain(
      'A route may have at most 100 stops.',
    );
  });
});
