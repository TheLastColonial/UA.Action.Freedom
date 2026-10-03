import type { JSX } from 'react';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useDonor, useDonorDonations, useEraseDonor } from '../../api/donations';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { LastChanged } from '../../components/LastChanged';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';

export function DonorDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const query = useDonor(id);
  const donations = useDonorDonations(id);
  const erase = useEraseDonor();
  const [confirming, setConfirming] = useState(false);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The donor could not be loaded.</p>;
  }

  const donor = query.data;
  const encodedId = encodeURIComponent(donor.id);

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>{donor.name}</h1>
        <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
          <LinkButton to={`/donors/${encodedId}/report`} variant="secondary">
            Donor report
          </LinkButton>
          <Gate policy="donations:write">
            <LinkButton to={`/donors/${encodedId}/edit`} variant="secondary">
              Edit
            </LinkButton>
          </Gate>
          <Gate policy="donors:erase">
            <Button
              variant="danger"
              disabled={erase.isPending || confirming}
              onClick={() => {
                setConfirming(true);
              }}
            >
              Erase donor
            </Button>
          </Gate>
        </span>
      </header>

      <LastChanged by={donor.lastChangedByName} at={donor.lastChangedAt} />

      {confirming ? (
        <div role="alertdialog" aria-labelledby="erase-heading" aria-describedby="erase-body">
          <h2 id="erase-heading">Erase this donor?</h2>
          <p id="erase-body">
            This permanently erases {donor.name}&apos;s personal details. Their donations, the items
            in them and their value are kept, and will show the donor as a former donor. It cannot
            be undone.
          </p>
          <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <Button
              variant="danger"
              disabled={erase.isPending}
              onClick={() => {
                erase.mutate(donor.id, {
                  onSuccess: () => {
                    void navigate('/donors');
                  },
                  onError: () => {
                    setConfirming(false);
                  },
                });
              }}
            >
              Erase permanently
            </Button>
            <Button
              variant="secondary"
              onClick={() => {
                setConfirming(false);
              }}
            >
              Cancel
            </Button>
          </span>
        </div>
      ) : null}

      {erase.isError ? (
        <p role="alert">
          {erase.error instanceof ApiDomainProblem
            ? (erase.error.detail ?? erase.error.message)
            : 'The donor could not be erased.'}
        </p>
      ) : null}

      <DetailCard title="Contact details">
        <dl>
          <dt>Email</dt>
          <dd>{donor.email ?? '—'}</dd>
          <dt>Phone</dt>
          <dd>{donor.phone ?? '—'}</dd>
        </dl>
      </DetailCard>

      <DetailCard title="Donations">
        <Gate policy="donations:write">
          <LinkButton to={`/donors/${encodedId}/donations/new`}>Record a donation</LinkButton>
        </Gate>
        {donations.isPending ? <PageSkeleton /> : null}
        {donations.isError ? <p role="alert">The donations could not be loaded.</p> : null}
        {donations.isSuccess ? (
          <DataTable
            caption="Donations"
            columns={[
              { header: 'Received', cell: (donation) => donation.receivedOn.slice(0, 10) },
              { header: 'Reference', cell: (donation) => `#${String(donation.id)}` },
              { header: 'Notes', cell: (donation) => donation.notes ?? '—' },
            ]}
            rows={donations.data}
            rowKey={(donation) => String(donation.id)}
            emptyMessage="No donations recorded yet."
          />
        ) : null}
      </DetailCard>
    </section>
  );
}
