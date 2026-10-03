import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateDonor } from '../../api/donations';
import { ApiDomainProblem, ApiValidationProblem } from '../../api/problem';
import { DonorForm } from './DonorForm';
import { donorFormToRequest, emptyDonorForm } from './donorFormModel';
import type { DonorFormValues } from './donorFormModel';

export function DonorCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateDonor();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;
  const fieldErrors =
    create.error instanceof ApiValidationProblem ? create.error.errors : undefined;

  const submit = (values: DonorFormValues) => {
    create.mutate(donorFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/donors/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New donor</h1>
      <DonorForm
        initialValues={emptyDonorForm()}
        submitLabel="Create donor"
        submitting={create.isPending}
        errorMessage={errorMessage}
        fieldErrors={fieldErrors}
        onSubmit={submit}
      />
    </section>
  );
}
