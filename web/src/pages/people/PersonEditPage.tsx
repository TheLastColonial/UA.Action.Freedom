import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { usePerson, useUpdatePerson } from '../../api/people';
import { ApiDomainProblem, ApiNotFound, ApiValidationProblem } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { PersonForm } from './PersonForm';
import { personFormToUpdateRequest, personToFormValues } from './personFormModel';
import type { PersonFormValues } from './personFormModel';

export function PersonEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const query = usePerson(id);
  const update = useUpdatePerson(id);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The volunteer could not be loaded.</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;
  const fieldErrors =
    update.error instanceof ApiValidationProblem ? update.error.errors : undefined;

  const submit = (values: PersonFormValues) => {
    update.mutate(personFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/people/${encodeURIComponent(id)}`);
      },
    });
  };

  return (
    <section>
      <h1>
        Edit {query.data.firstName} {query.data.lastName}
      </h1>
      <PersonForm
        initialValues={personToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        fieldErrors={fieldErrors}
        onSubmit={submit}
      />
    </section>
  );
}
