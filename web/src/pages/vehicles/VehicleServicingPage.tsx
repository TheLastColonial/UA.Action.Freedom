import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { ApiNotFound } from '../../api/problem';
import { useRecordInspection, useVehicle } from '../../api/vehicles';
import { Button } from '../../components/Button';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { INSPECTION_STATUS_LABELS } from './inspection';
import { VehicleServicingForm } from './VehicleServicingForm';

export function VehicleServicingPage(): JSX.Element {
  const { vin = '' } = useParams();
  const navigate = useNavigate();
  const query = useVehicle(vin);
  const record = useRecordInspection(vin);

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
      <p>
        <strong>Servicing status:</strong>{' '}
        {vehicle.servicing ? 'In for servicing' : 'Not currently servicing'}
      </p>
      <p>
        <strong>Inspection status:</strong>{' '}
        <span>{INSPECTION_STATUS_LABELS[vehicle.inspectionStatus]}</span>
      </p>

      {record.isSuccess ? <p role="status">Inspection saved.</p> : null}

      <Gate
        policy="vehicles:service"
        fallback={
          <p>
            <strong>Inspection notes:</strong> <span>{vehicle.inspectionNotes ?? '—'}</span>
          </p>
        }
      >
        <VehicleServicingForm
          vin={vehicle.vin}
          initialStatus={vehicle.inspectionStatus}
          initialNotes={vehicle.inspectionNotes}
          submitting={record.isPending}
          errorMessage={record.isError ? record.error.message : undefined}
          onSubmit={(body) => {
            record.mutate(body);
          }}
        />
      </Gate>

      <Button
        variant="secondary"
        onClick={() => {
          void navigate(`/vehicles/${encodeURIComponent(vehicle.vin)}`);
        }}
      >
        Back to vehicle
      </Button>
    </section>
  );
}
