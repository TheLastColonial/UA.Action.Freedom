import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { ApiNotFound } from '../../api/problem';
import { useDeleteVehicle, useVehicle } from '../../api/vehicles';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';

export function VehicleDetailPage(): JSX.Element {
  const { vin = '' } = useParams();
  const navigate = useNavigate();
  const query = useVehicle(vin);
  const remove = useDeleteVehicle();

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The vehicle could not be loaded. {query.error.message}</p>;
  }

  const vehicle = query.data;

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>{vehicle.vin}</h1>
        <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
          <LinkButton to={`/vehicles/${encodeURIComponent(vehicle.vin)}/servicing`} variant="secondary">
            Servicing
          </LinkButton>
          <Gate policy="vehicles:write">
            <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
              <LinkButton to={`/vehicles/${encodeURIComponent(vehicle.vin)}/edit`} variant="secondary">
                Edit
              </LinkButton>
              <Button
                variant="danger"
                disabled={remove.isPending}
                onClick={() => {
                  remove.mutate(vehicle.vin, {
                    onSuccess: () => {
                      void navigate('/vehicles');
                    },
                  });
                }}
              >
                Delete
              </Button>
            </span>
          </Gate>
        </span>
      </header>

      {remove.isError ? <p role="alert">{remove.error.message}</p> : null}

      <DetailCard title="Vehicle details">
        <dl>
          <dt>Number plate</dt>
          <dd>{vehicle.plate}</dd>
          <dt>Make / model</dt>
          <dd>{[vehicle.brand, vehicle.model].filter(Boolean).join(' ') || '—'}</dd>
          <dt>Colour</dt>
          <dd>{vehicle.colour ?? '—'}</dd>
          <dt>Year</dt>
          <dd>{vehicle.year}</dd>
        </dl>
      </DetailCard>

      <DetailCard title="Engine details">
        <dl>
          <dt>Transmission</dt>
          <dd>{vehicle.transmission}</dd>
          <dt>Fuel</dt>
          <dd>{vehicle.fuel}</dd>
          <dt>Kerb weight</dt>
          <dd>{vehicle.weightKg} kg</dd>
          <dt>Mileage</dt>
          <dd>{vehicle.mileage ?? '—'}</dd>
        </dl>
      </DetailCard>

      <DetailCard title="Cargo capacity">
        <dl>
          <dt>Maximum cargo weight</dt>
          <dd>{vehicle.maxCargoWeightKg === null ? '—' : `${vehicle.maxCargoWeightKg} kg`}</dd>
          <dt>Cargo dimensions (W × D × H)</dt>
          <dd>
            {vehicle.cargoWidthCm === null && vehicle.cargoDepthCm === null && vehicle.cargoHeightCm === null
              ? '—'
              : `${vehicle.cargoWidthCm ?? '—'} × ${vehicle.cargoDepthCm ?? '—'} × ${vehicle.cargoHeightCm ?? '—'} cm`}
          </dd>
        </dl>
      </DetailCard>

      <DetailCard title="Status">
        <dl>
          <dt>Convoy</dt>
          <dd>{vehicle.convoyId ?? 'Unassigned'}</dd>
          <dt>In for servicing</dt>
          <dd>{vehicle.servicing ? 'Yes' : 'No'}</dd>
        </dl>
      </DetailCard>

      <DetailCard title="Purchase information">
        <dl>
          <dt>Purchaser</dt>
          <dd>{vehicle.purchaserName ?? '—'}</dd>
          <dt>Purchase date</dt>
          <dd>{vehicle.purchaseDate ? vehicle.purchaseDate.slice(0, 10) : '—'}</dd>
        </dl>
      </DetailCard>

      <DetailCard title="Notes">
        <dl>
          <dt>Notes</dt>
          <dd>{vehicle.notes ?? '—'}</dd>
        </dl>
      </DetailCard>
    </section>
  );
}
