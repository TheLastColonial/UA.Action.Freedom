import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateReceiver } from '../../api/receivers';
import { ApiDomainProblem } from '../../api/problem';
import { ReceiverForm } from './ReceiverForm';
import { emptyReceiverForm, receiverFormToRequest } from './receiverModels';
import type { ReceiverFormValues } from './receiverModels';

export function ReceiverCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateReceiver();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: ReceiverFormValues) => {
    create.mutate(receiverFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/receivers/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New receiver</h1>
      <ReceiverForm
        initialValues={emptyReceiverForm()}
        submitLabel="Create receiver"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
