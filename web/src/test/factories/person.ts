import type { PersonReadModel } from '../../api/schemas/people';

let seq = 0;

export function makePerson(overrides: Partial<PersonReadModel> = {}): PersonReadModel {
  seq += 1;
  return {
    id: `00000000-0000-0000-0000-${String(seq).padStart(12, '0')}`,
    firstName: `Person${String(seq)}`,
    lastName: 'Volunteer',
    dateOfBirth: '1990-01-01T00:00:00',
    joined: '2024-01-01T00:00:00',
    phone: null,
    isDriver: false,
    committed: false,
    ...overrides,
  };
}
