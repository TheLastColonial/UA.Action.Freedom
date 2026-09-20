import type { JSX } from 'react';
import { useNavigate, useParams } from 'react-router-dom';

import { useCreateManifestForVehicle } from '../../api/convoys';
import { ApiDomainProblem } from '../../api/problem';
import { ManifestForm } from './ManifestForm';
import { emptyManifestForm, manifestFormToRequest } from './manifestModels';
import type { ManifestFormValues } from './manifestModels';

/**
 * Opens a manifest against one vehicle on one convoy.
 *
 * The convoy and the VIN come from the route because that pair is what the manifest is the
 * paperwork for — there is no `POST /manifests`, and no form field to point one at a truck that
 * is on a different convoy, or none.
 */
export function ManifestCreatePage(): JSX.Element {
  const navigate = useNavigate();
  const params = useParams();
  const convoyId = Number(params['convoyId']);
  const vin = String(params['vin']);
  const create = useCreateManifestForVehicle(convoyId, vin);

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
      <h1>New manifest for {vin}</h1>
      <ManifestForm
        mode="create"
        initialValues={emptyManifestForm()}
        submitLabel="Open manifest"
        submitting={create.isPending}
        errorMessage={errorMessage}
        onSubmit={submit}
      />
    </section>
  );
}
