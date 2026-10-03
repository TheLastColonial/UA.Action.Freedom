import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useDonor, useUpdateDonor } from '../../api/donations';
import { ApiDomainProblem, ApiNotFound, ApiValidationProblem } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { DonorForm } from './DonorForm';
import { donorFormToRequest, donorToFormValues } from './donorFormModel';
import type { DonorFormValues } from './donorFormModel';

export function DonorEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const query = useDonor(id);
  const update = useUpdateDonor(id);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The donor could not be loaded.</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;
  const fieldErrors =
    update.error instanceof ApiValidationProblem ? update.error.errors : undefined;

  const submit = (values: DonorFormValues) => {
    update.mutate(donorFormToRequest(values), {
      onSuccess: () => {
        void navigate(`/donors/${encodeURIComponent(id)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit {query.data.name}</h1>
      <DonorForm
        initialValues={donorToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        fieldErrors={fieldErrors}
        onSubmit={submit}
      />
    </section>
  );
}
