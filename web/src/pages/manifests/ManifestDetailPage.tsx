import type { JSX } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useDeleteManifest, useManifest } from '../../api/manifests';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { ManifestBoxesPanel } from './ManifestBoxesPanel';
import { ManifestStatePanel } from './ManifestStatePanel';
import { ManifestTeamsPanel } from './ManifestTeamsPanel';
import { ManifestWeightPanel } from './ManifestWeightPanel';

type Tab = 'overview' | 'status' | 'teams' | 'cargo' | 'weight';
const TABS: readonly Tab[] = ['overview', 'status', 'teams', 'cargo', 'weight'];
const TAB_LABEL: Record<Tab, string> = {
  overview: 'Overview',
  status: 'Status',
  teams: 'Teams',
  cargo: 'Cargo',
  weight: 'Weight',
};

export function ManifestDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const rawTab = searchParams.get('tab');
  const tab: Tab = TABS.includes(rawTab as Tab) ? (rawTab as Tab) : 'overview';

  const navigate = useNavigate();
  const query = useManifest(id);
  const remove = useDeleteManifest();

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The manifest could not be loaded.</p>;
  }

  const manifest = query.data;
  const deleteError =
    remove.error instanceof ApiDomainProblem
      ? (remove.error.detail ?? remove.error.message)
      : undefined;

  const selectTab = (next: Tab) => {
    setSearchParams((params) => {
      params.set('tab', next);
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>{manifest.id}</h1>
        <span>
          {manifest.status}
          {manifest.frozen ? ' · frozen' : ''}
        </span>
      </header>

      <nav aria-label="Manifest sections" style={{ display: 'flex', gap: 'var(--space-3)' }}>
        {TABS.map((name) => (
          <button
            key={name}
            type="button"
            aria-current={tab === name ? 'page' : undefined}
            onClick={() => {
              selectTab(name);
            }}
          >
            {TAB_LABEL[name]}
          </button>
        ))}
      </nav>

      {tab === 'overview' ? (
        <div>
          <dl>
            <dt>Vehicle</dt>
            <dd>{manifest.vin ?? '—'}</dd>
            <dt>Convoy</dt>
            <dd>{manifest.convoyId ?? 'Not linked'}</dd>
            <dt>Ferry booking</dt>
            <dd>{manifest.ferryBookingComplete ? 'Complete' : 'Outstanding'}</dd>
            <dt>Delivery notes</dt>
            <dd>{manifest.deliveryNotes ?? '—'}</dd>
          </dl>
          <Gate policy="manifests:write">
            <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
              {!manifest.frozen ? (
                <Link to={`/manifests/${encodeURIComponent(manifest.id)}/edit`}>Edit</Link>
              ) : null}
              <button
                type="button"
                disabled={remove.isPending}
                onClick={() => {
                  remove.mutate(manifest.id, {
                    onSuccess: () => {
                      void navigate('/manifests');
                    },
                  });
                }}
              >
                Delete
              </button>
            </span>
          </Gate>
          {deleteError ? (
            <p role="alert" className="field__error">
              {deleteError}
            </p>
          ) : null}
        </div>
      ) : null}

      {tab === 'status' ? <ManifestStatePanel manifest={manifest} /> : null}
      {tab === 'teams' ? (
        <ManifestTeamsPanel manifestId={manifest.id} frozen={manifest.frozen} />
      ) : null}
      {tab === 'cargo' ? (
        <ManifestBoxesPanel manifestId={manifest.id} frozen={manifest.frozen} />
      ) : null}
      {tab === 'weight' ? <ManifestWeightPanel manifestId={manifest.id} /> : null}
    </section>
  );
}
