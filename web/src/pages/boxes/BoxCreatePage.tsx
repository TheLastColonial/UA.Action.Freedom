import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateBox } from '../../api/boxes';
import { ApiDomainProblem } from '../../api/problem';
import { BoxForm } from './BoxForm';
import { boxFormToRequest, emptyBoxForm } from './boxModels';
import type { BoxFormValues } from './boxModels';

export function BoxCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateBox();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: BoxFormValues) => {
    create.mutate(boxFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/boxes/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New box</h1>
      <BoxForm
        initialValues={emptyBoxForm()}
        submitLabel="Create box"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
