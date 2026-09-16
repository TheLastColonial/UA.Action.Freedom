import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useDeleteLocation, useLocation } from '../../api/locations';
import { ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { BaysPanel } from './BaysPanel';

export function LocationDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const locationId = Number(id);
  const navigate = useNavigate();
  const query = useLocation(locationId);
  const remove = useDeleteLocation();

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The location could not be loaded.</p>;
  }

  const location = query.data;

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>{location.name}</h1>
        <Gate policy="locations:write">
          <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <LinkButton to={`/locations/${String(location.id)}/edit`} variant="secondary">
              Edit
            </LinkButton>
            <Button
              variant="danger"
              disabled={remove.isPending}
              onClick={() => {
                remove.mutate(location.id, {
                  onSuccess: () => {
                    void navigate('/locations');
                  },
                });
              }}
            >
              Delete
            </Button>
          </span>
        </Gate>
      </header>

      {remove.isError ? <p role="alert">The location could not be removed.</p> : null}

      <DetailCard title="Address">
        <dl>
          <dt>Address</dt>
          <dd>
            {[location.house, location.street, location.city, location.country, location.postcode]
              .filter(Boolean)
              .join(', ') || '—'}
          </dd>
        </dl>
      </DetailCard>

      <BaysPanel locationId={location.id} />
    </section>
  );
}
