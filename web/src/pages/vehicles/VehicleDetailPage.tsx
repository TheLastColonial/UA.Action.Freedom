import type { JSX } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { ApiNotFound } from '../../api/problem';
import { useDeleteVehicle, useVehicle } from '../../api/vehicles';
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
        <Gate policy="vehicles:write">
          <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
            <Link to={`/vehicles/${encodeURIComponent(vehicle.vin)}/edit`}>Edit</Link>
            <button
              type="button"
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
            </button>
          </span>
        </Gate>
      </header>

      {remove.isError ? <p role="alert">{remove.error.message}</p> : null}

      <dl>
        <dt>Number plate</dt>
        <dd>{vehicle.plate}</dd>
        <dt>Make / model</dt>
        <dd>{[vehicle.brand, vehicle.model].filter(Boolean).join(' ') || '—'}</dd>
        <dt>Colour</dt>
        <dd>{vehicle.colour ?? '—'}</dd>
        <dt>Year</dt>
        <dd>{vehicle.year}</dd>
        <dt>Transmission</dt>
        <dd>{vehicle.transmission}</dd>
        <dt>Fuel</dt>
        <dd>{vehicle.fuel}</dd>
        <dt>Kerb weight</dt>
        <dd>{vehicle.weightKg} kg</dd>
        <dt>Mileage</dt>
        <dd>{vehicle.mileage ?? '—'}</dd>
        <dt>In for servicing</dt>
        <dd>{vehicle.servicing ? 'Yes' : 'No'}</dd>
        <dt>Convoy</dt>
        <dd>{vehicle.convoyId ?? 'Unassigned'}</dd>
        <dt>Purchaser</dt>
        <dd>{vehicle.purchaserName ?? '—'}</dd>
        <dt>Notes</dt>
        <dd>{vehicle.notes ?? '—'}</dd>
      </dl>
    </section>
  );
}
