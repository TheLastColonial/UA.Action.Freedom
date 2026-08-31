import type { JSX } from 'react';
import { useNavigate } from 'react-router-dom';

import { useCreateManifest } from '../../api/manifests';
import { ApiDomainProblem } from '../../api/problem';
import { ManifestForm } from './ManifestForm';
import { emptyManifestForm, manifestFormToRequest } from './manifestModels';
import type { ManifestFormValues } from './manifestModels';

export function ManifestCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const create = useCreateManifest();

  const errorMessage =
    create.error instanceof ApiDomainProblem
      ? (create.error.detail ?? create.error.message)
      : undefined;

  const submit = (values: ManifestFormValues) => {
    create.mutate(manifestFormToRequest(values), {
      onSuccess: (created) => {
        void navigate(`/manifests/${encodeURIComponent(created.id)}`);
      },
    });
  };

  return (
    <section>
      <h1>New manifest</h1>
      <ManifestForm
        mode="create"
        initialValues={emptyManifestForm()}
        submitLabel="Create manifest"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
