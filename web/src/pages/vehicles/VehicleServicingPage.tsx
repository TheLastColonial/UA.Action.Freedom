import type { JSX } from 'react';
import { Link, useParams } from 'react-router-dom';

import { ApiNotFound } from '../../api/problem';
import { useVehicle } from '../../api/vehicles';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';

export function VehicleServicingPage(): JSX.Element {
  const { vin = '' } = useParams();
  const query = useVehicle(vin);

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
      <h1>Servicing — {vehicle.vin}</h1>
      <p>In for servicing: {vehicle.servicing ? 'Yes' : 'No'}</p>
      <p>The servicing workflow is coming in a future update.</p>
      <Link to={`/vehicles/${encodeURIComponent(vehicle.vin)}`}>Back to vehicle</Link>
    </section>
  );
}
