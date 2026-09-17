import type { JSX } from 'react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { assignBoxBay, useCreateBox } from '../../api/boxes';
import { ApiDomainProblem } from '../../api/problem';
import { BoxForm } from './BoxForm';
import { boxFormToRequest, emptyBoxForm } from './boxModels';
import type { BoxFormValues } from './boxModels';

export function BoxCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateBox();
  const [bayPlacementFailed, setBayPlacementFailed] = useState(false);

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: BoxFormValues) => {
    const bayId = values.bayId.trim();
    const assignedByPersonId = values.assignedByPersonId.trim();

    create.mutate(boxFormToRequest(values), {
      onSuccess: (created) => {
        if (!bayId || !assignedByPersonId) {
          void navigate(`/boxes/${encodeURIComponent(created.id)}`);
          return;
        }

        // The box exists either way by this point, so a failed placement is not a failed
        // creation — land on the box's own page, where BoxBayPanel can finish the job.
        void assignBoxBay(Number(created.id), { bayId: Number(bayId), assignedByPersonId })
          .catch(() => {
            setBayPlacementFailed(true);
          })
          .then(() => navigate(`/boxes/${encodeURIComponent(created.id)}`));
      },
    });
  };

  return (
    <section>
      <h1>New box</h1>
      {bayPlacementFailed ? (
        <p role="alert">
          The box was created, but it could not be placed in that bay — place it from the box's page
          instead.
        </p>
      ) : null}
      <BoxForm
        initialValues={emptyBoxForm()}
        submitLabel="Create box"
        submitting={create.isPending}
        errorMessage={errorMessage}
        enableBayAssignment
        onSubmit={submit}
      />
    </section>
  );
}
