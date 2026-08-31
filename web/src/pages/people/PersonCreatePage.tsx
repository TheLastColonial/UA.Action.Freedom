import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreatePerson } from '../../api/people';
import { ApiDomainProblem, ApiValidationProblem } from '../../api/problem';
import { PersonForm } from './PersonForm';
import { emptyPersonForm, personFormToRequest } from './personFormModel';
import type { PersonFormValues } from './personFormModel';

export function PersonCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreatePerson();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;
  const fieldErrors =
    create.error instanceof ApiValidationProblem ? create.error.errors : undefined;

  const submit = (values: PersonFormValues) => {
    create.mutate(personFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/people/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New volunteer</h1>
      <PersonForm
        initialValues={emptyPersonForm()}
        submitLabel="Create volunteer"
        submitting={create.isPending}
        errorMessage={errorMessage}
        fieldErrors={fieldErrors}
        onSubmit={submit}
      />
    </section>
  );
}
