import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useBox, useUpdateBox } from '../../api/boxes';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { BoxForm } from './BoxForm';
import { boxFormToUpdateRequest, boxToFormValues } from './boxModels';
import type { BoxFormValues } from './boxModels';

export function BoxEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const boxId = Number(id);
  const navigate = useNavigate();
  const query = useBox(boxId);
  const update = useUpdateBox(boxId);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The box could not be loaded.</p>;
  }

  if (query.data.validated) {
    return (
      <section>
        <h1>Box #{boxId}</h1>
        <p role="status">This box has been validated and can no longer be edited.</p>
      </section>
    );
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;

  const submit = (values: BoxFormValues) => {
    update.mutate(boxFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/boxes/${String(boxId)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit box #{boxId}</h1>
      <BoxForm
        initialValues={boxToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
