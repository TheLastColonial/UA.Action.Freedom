import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useManifest, useUpdateManifest } from '../../api/manifests';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ManifestForm } from './ManifestForm';
import { manifestFormToUpdateRequest, manifestToFormValues } from './manifestModels';
import type { ManifestFormValues } from './manifestModels';

export function ManifestEditPage(): JSX.Element {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const query = useManifest(id);
  const update = useUpdateManifest(id);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The manifest could not be loaded.</p>;
  }

  if (query.data.frozen) {
    return (
      <section>
        <h1>{query.data.id}</h1>
        <p role="status">
          A Goods Movement Reference has been created for this manifest — it can no longer be
          edited.
        </p>
      </section>
    );
  }

  const errorMessage =
    update.error instanceof ApiDomainProblem
      ? (update.error.detail ?? update.error.message)
      : undefined;

  const submit = (values: ManifestFormValues) => {
    update.mutate(manifestFormToUpdateRequest(values), {
      onSuccess: () => {
        void navigate(`/manifests/${encodeURIComponent(id)}`);
      },
    });
  };

  return (
    <section>
      <h1>Edit {query.data.id}</h1>
      <ManifestForm
        mode="edit"
        initialValues={manifestToFormValues(query.data)}
        submitLabel="Save changes"
        submitting={update.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
