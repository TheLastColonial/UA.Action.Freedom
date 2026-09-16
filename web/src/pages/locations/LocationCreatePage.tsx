import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateLocation } from '../../api/locations';
import { ApiDomainProblem } from '../../api/problem';
import { LocationForm } from './LocationForm';
import { emptyLocationForm, locationFormToRequest } from './locationModels';
import type { LocationFormValues } from './locationModels';

export function LocationCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateLocation();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: LocationFormValues) => {
    create.mutate(locationFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/locations/${created.id}`);
      },
    });
  };

  return (
    <section>
      <h1>New location</h1>
      <LocationForm
        initialValues={emptyLocationForm()}
        submitLabel="Create location"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
