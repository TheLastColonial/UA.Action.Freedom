import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useLocation, useUpdateLocation } from '../../api/locations';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { LocationForm } from './LocationForm';
import { locationFormToUpdateRequest, locationToFormValues } from './locationModels';
import type { LocationFormValues } from './locationModels';

export function LocationEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const locationId = Number(id);
  const navigate = useNavigate();
  const query = useLocation(locationId);
  const update = useUpdateLocation(locationId);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The location could not be loaded.</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;

  const submit = (values: LocationFormValues) => {
    update.mutate(locationFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/locations/${String(locationId)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit {query.data.name}</h1>
      <LocationForm
        initialValues={locationToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
