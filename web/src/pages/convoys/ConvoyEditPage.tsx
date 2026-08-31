import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useConvoy, useUpdateConvoy } from '../../api/convoys';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ConvoyForm } from './ConvoyForm';
import { convoyFormToUpdateRequest, convoyToFormValues } from './convoyFormModel';
import type { ConvoyFormValues } from './convoyFormModel';

export function ConvoyEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const convoyId = Number(id);
  const navigate = useNavigate();
  const query = useConvoy(convoyId);
  const update = useUpdateConvoy(convoyId);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The convoy could not be loaded.</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;

  const submit = (values: ConvoyFormValues) => {
    update.mutate(convoyFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/convoys/${String(convoyId)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit convoy #{convoyId}</h1>
      <ConvoyForm
        initialValues={convoyToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
