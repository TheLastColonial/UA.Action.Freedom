import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useReceiver, useUpdateReceiver } from '../../api/receivers';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ReceiverForm } from './ReceiverForm';
import { receiverFormToUpdateRequest, receiverToFormValues } from './receiverModels';
import type { ReceiverFormValues } from './receiverModels';

export function ReceiverEditPage(): JSX.Element {
  const { ref = '' } = useParams();
  const navigate = useNavigate();
  const query = useReceiver(ref);
  const update = useUpdateReceiver(ref);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The receiver could not be loaded.</p>;
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;

  const submit = (values: ReceiverFormValues) => {
    update.mutate(receiverFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/receivers/${encodeURIComponent(ref)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit {query.data.organisation}</h1>
      <ReceiverForm
        initialValues={receiverToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
