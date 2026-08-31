import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateConvoy } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { ConvoyForm } from './ConvoyForm';
import { convoyFormToRequest, emptyConvoyForm } from './convoyFormModel';
import type { ConvoyFormValues } from './convoyFormModel';

export function ConvoyCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateConvoy();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: ConvoyFormValues) => {
    create.mutate(convoyFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/convoys/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New convoy</h1>
      <ConvoyForm
        initialValues={emptyConvoyForm()}
        submitLabel="Create convoy"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
