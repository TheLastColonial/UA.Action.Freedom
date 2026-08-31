import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { ApiDomainProblem, ApiNotFound, ApiValidationProblem } from '../../api/problem';
import { useUpdateVehicle, useVehicle } from '../../api/vehicles';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { VehicleForm } from './VehicleForm';
import { vehicleFormToUpdateRequest, vehicleToFormValues } from './vehicleFormModel';
import type { VehicleFormValues } from './vehicleFormModel';

export function VehicleEditPage(): JSX.Element {
  const { vin = '' } = useParams();
  const navigate = useNavigate();
  const query = useVehicle(vin);
  const update = useUpdateVehicle(vin);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The vehicle could not be loaded. {query.error.message}</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;
  const fieldErrors =
    update.error instanceof ApiValidationProblem ? update.error.errors : undefined;

  const submit = (values: VehicleFormValues) => {
    update.mutate(vehicleFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/vehicles/${encodeURIComponent(vin)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit {query.data.vin}</h1>
      <VehicleForm
        mode="edit"
        initialValues={vehicleToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        fieldErrors={fieldErrors}
        onSubmit={submit}
      />
    </section>
  );
}
