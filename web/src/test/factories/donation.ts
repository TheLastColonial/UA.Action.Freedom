import type { DonationReadModel, DonorReadModel } from '../../api/schemas/donations';

let donorSeq = 0;
let donationSeq = 0;

export function makeDonor(overrides: Partial<DonorReadModel> = {}): DonorReadModel {
  donorSeq += 1;
  return {
    id: `dddddddd-0000-0000-0000-${String(donorSeq).padStart(12, '0')}`,
    name: `Donor ${String(donorSeq)}`,
    email: null,
    phone: null,
    lastChangedByName: null,
    lastChangedAt: null,
    ...overrides,
  };
}

export function makeDonation(
  donorId: string,
  overrides: Partial<DonationReadModel> = {},
): DonationReadModel {
  donationSeq += 1;
  return {
    id: donationSeq,
    donorId,
    donorName: 'Donor',
    receivedOn: '2026-09-20T00:00:00',
    notes: null,
    lastChangedByName: null,
    lastChangedAt: null,
    ...overrides,
  };
}
