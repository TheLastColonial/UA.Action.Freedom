import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { ApiDomainProblem, ApiValidationProblem } from '../../api/problem';
import { useCreateVehicle } from '../../api/vehicles';
import { VehicleForm } from './VehicleForm';
import { emptyVehicleForm, vehicleFormToRequest } from './vehicleFormModel';
import type { VehicleFormValues } from './vehicleFormModel';

export function VehicleCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateVehicle();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;
  const fieldErrors =
    create.error instanceof ApiValidationProblem ? create.error.errors : undefined;

  const submit = (values: VehicleFormValues) => {
    create.mutate(vehicleFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/vehicles/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New vehicle</h1>
      <VehicleForm
        mode="create"
        initialValues={emptyVehicleForm()}
        submitLabel="Create vehicle"
        submitting={create.isPending}
        errorMessage={errorMessage}
        fieldErrors={fieldErrors}
        onSubmit={submit}
      />
    </section>
  );
}
