import type { JSX } from 'react';

import { useConvoy } from '../../api/convoys';
import { useTransitionManifest } from '../../api/manifests';
import { ApiDomainProblem } from '../../api/problem';
import type { ManifestReadModel } from '../../api/schemas/manifests';
import { useAuth } from '../../auth/useAuth';
import { availableTransitions } from './transitions';

interface ManifestStatePanelProps {
  manifest: ManifestReadModel;
}

export function ManifestStatePanel({ manifest }: ManifestStatePanelProps): JSX.Element {
  const auth = useAuth();
  const transition = useTransitionManifest(manifest.id);
  const convoy = useConvoy(manifest.convoyId ?? 0, { enabled: manifest.convoyId !== null });

  const options = availableTransitions({
    status: manifest.status,
    frozen: manifest.frozen,
    canApprove: auth.hasPolicy('manifests:approve'),
    hasConvoy: manifest.convoyId !== null,
    convoyTruckListPublished: convoy.data?.truckListPublished ?? false,
  });

  const errorMessage =
    transition.error instanceof ApiDomainProblem
      ? (transition.error.detail ?? transition.error.message)
      : undefined;
  const justApproved = transition.isSuccess && transition.variables === 'approve';

  return (
    <div>
      <h2>Status: {manifest.status}</h2>
      {manifest.frozen ? (
        <p role="status">Frozen — a Goods Movement Reference has been submitted.</p>
      ) : null}
      {errorMessage ? (
        <p role="alert" className="field__error">
          {errorMessage}
        </p>
      ) : null}
      {justApproved ? <p role="status">GMR submitted — the manifest is now frozen.</p> : null}

      {options.length === 0 ? <p>No further transitions are available from here.</p> : null}

      <ul
        style={{
          listStyle: 'none',
          padding: 0,
          display: 'flex',
          flexWrap: 'wrap',
          gap: 'var(--space-2)',
        }}
      >
        {options.map((option) => (
          <li key={option.verb}>
            <button
              type="button"
              disabled={option.disabledReason !== null || transition.isPending}
              onClick={() => {
                transition.mutate(option.verb);
              }}
            >
              {option.label}
            </button>
            {option.disabledReason ? <p className="field__hint">{option.disabledReason}</p> : null}
          </li>
        ))}
      </ul>
    </div>
  );
}
