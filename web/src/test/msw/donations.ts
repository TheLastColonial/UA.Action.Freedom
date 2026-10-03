import { HttpResponse, http } from 'msw';
import type { RequestHandler } from 'msw';

import type {
  CreateDonationRequest,
  DonationReadModel,
  DonorReadModel,
  DonorReport,
  DonorRequest,
} from '../../api/schemas/donations';
import { problem } from './problem';

export interface DonationApi {
  donors: Map<string, DonorReadModel>;
  donations: Map<number, DonationReadModel>;
  handlers: RequestHandler[];
}

let mintedDonor = 0;
let mintedDonation = 1000;

const FORMER_DONOR = 'Former donor';

/**
 * Donors and donations as the API serves them. Erasing a donor removes their details but keeps their
 * donations, which then name "Former donor", as the real API does.
 */
export function donationApi(
  seedDonors: readonly DonorReadModel[] = [],
  seedDonations: readonly DonationReadModel[] = [],
  report?: (donorName: string) => DonorReport,
): DonationApi {
  const donors = new Map(seedDonors.map((donor) => [donor.id, donor]));
  const donations = new Map(seedDonations.map((donation) => [donation.id, donation]));
  const known = new Set(seedDonors.map((donor) => donor.id));

  const donorName = (donorId: string): string | undefined => {
    const donor = donors.get(donorId);
    if (donor) {
      return donor.name;
    }
    return known.has(donorId) ? FORMER_DONOR : undefined;
  };

  const named = (donation: DonationReadModel): DonationReadModel => ({
    ...donation,
    donorName: donorName(donation.donorId) ?? FORMER_DONOR,
  });

  const handlers: RequestHandler[] = [
    http.get('/donors', () => HttpResponse.json([...donors.values()])),

    http.get('/donors/:id', ({ params }) => {
      const donor = donors.get(String(params['id']));
      return donor ? HttpResponse.json(donor) : new HttpResponse(null, { status: 404 });
    }),

    http.post('/donors', async ({ request }) => {
      mintedDonor += 1;
      const id = `eeeeeeee-0000-0000-0000-${String(mintedDonor).padStart(12, '0')}`;
      const body = (await request.json()) as DonorRequest;
      donors.set(id, {
        id,
        name: body.name,
        email: body.email ?? null,
        phone: body.phone ?? null,
        lastChangedByName: null,
        lastChangedAt: null,
      });
      known.add(id);
      return new HttpResponse(null, { status: 201, headers: { Location: `/donors/${id}` } });
    }),

    http.put('/donors/:id', async ({ params, request }) => {
      const id = String(params['id']);
      const donor = donors.get(id);
      if (!donor) {
        return new HttpResponse(null, { status: 404 });
      }
      const body = (await request.json()) as DonorRequest;
      donors.set(id, {
        ...donor,
        name: body.name,
        email: body.email ?? null,
        phone: body.phone ?? null,
      });
      return new HttpResponse(null, { status: 204 });
    }),

    http.delete('/donors/:id', ({ params }) =>
      donors.delete(String(params['id']))
        ? new HttpResponse(null, { status: 204 })
        : new HttpResponse(null, { status: 404 }),
    ),

    http.get('/donors/:id/donations', ({ params }) => {
      const id = String(params['id']);
      if (donorName(id) === undefined) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(
        [...donations.values()].filter((donation) => donation.donorId === id).map(named),
      );
    }),

    http.get('/donors/:id/report', ({ params }) => {
      const name = donorName(String(params['id']));
      if (name === undefined) {
        return new HttpResponse(null, { status: 404 });
      }
      return HttpResponse.json(
        report?.(name) ?? {
          donorId: String(params['id']),
          donorName: name,
          itemCount: 0,
          totalValueGbp: 0,
          byCategory: [],
          donations: [],
        },
      );
    }),

    http.get('/donations', () => HttpResponse.json([...donations.values()].map(named))),

    http.post('/donations', async ({ request }) => {
      const body = (await request.json()) as CreateDonationRequest;
      if (!donors.has(body.donorId)) {
        return problem(422, 'The donor named is not on file. Enter the donor first.', {
          type: 'donor-not-found',
        });
      }
      mintedDonation += 1;
      donations.set(mintedDonation, {
        id: mintedDonation,
        donorId: body.donorId,
        donorName: donorName(body.donorId) ?? FORMER_DONOR,
        receivedOn: `${body.receivedOn}T00:00:00`,
        notes: body.notes ?? null,
        lastChangedByName: null,
        lastChangedAt: null,
      });
      return new HttpResponse(null, {
        status: 201,
        headers: { Location: `/donations/${String(mintedDonation)}` },
      });
    }),
  ];

  return { donors, donations, handlers };
}
