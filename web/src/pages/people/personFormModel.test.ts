import { describe, expect, it } from 'vitest';

import type { PersonReadModel } from '../../api/schemas/people';
import {
  emptyPersonForm,
  personFormSchema,
  personFormToRequest,
  personToFormValues,
} from './personFormModel';

const base = {
  ...emptyPersonForm(),
  firstName: '  Olena ',
  lastName: 'Kovalenko',
  dateOfBirth: '1991-03-04',
  joined: '2024-06-01',
  phone: ' 07700 900123 ',
  isDriver: true,
  committed: true,
};

describe('personFormToRequest', () => {
  it('trims names and phone and passes the dates through', () => {
    const request = personFormToRequest(base);

    expect(request.firstName).toBe('Olena');
    expect(request.phone).toBe('07700 900123');
    expect(request.dateOfBirth).toBe('1991-03-04');
  });

  it('drops an empty phone', () => {
    expect('phone' in personFormToRequest({ ...base, phone: '   ' })).toBe(false);
  });

  it('never marks a non-driver as committed', () => {
    const request = personFormToRequest({ ...base, isDriver: false, committed: true });
    expect(request.committed).toBe(false);
  });
});

describe('personToFormValues', () => {
  it('reduces ISO timestamps to calendar dates', () => {
    const person: PersonReadModel = {
      id: 'p1',
      firstName: 'A',
      lastName: 'B',
      dateOfBirth: '1990-01-02T00:00:00',
      joined: '2023-09-10T00:00:00',
      phone: null,
      isDriver: false,
      committed: false,
    };

    const values = personToFormValues(person);
    expect(values.dateOfBirth).toBe('1990-01-02');
    expect(values.joined).toBe('2023-09-10');
    expect(values.phone).toBe('');
  });
});

describe('personFormSchema', () => {
  it('accepts a well-formed volunteer', () => {
    expect(personFormSchema.safeParse(base).success).toBe(true);
  });

  it('requires both names', () => {
    const result = personFormSchema.safeParse({ ...base, firstName: '', lastName: '' });
    const messages = result.error?.issues.map((i) => i.message) ?? [];
    expect(messages).toContain('First name is required');
    expect(messages).toContain('Last name is required');
  });

  it('rejects committed without isDriver, against the committed field', () => {
    const result = personFormSchema.safeParse({ ...base, isDriver: false, committed: true });
    expect(result.success).toBe(false);
    const issue = result.error?.issues[0];
    expect(issue?.path).toEqual(['committed']);
    expect(issue?.message).toBe("'Committed' can only be set for a volunteer who drives.");
  });

  it('rejects a malformed date of birth', () => {
    expect(personFormSchema.safeParse({ ...base, dateOfBirth: '04/03/1991' }).success).toBe(false);
  });
});
