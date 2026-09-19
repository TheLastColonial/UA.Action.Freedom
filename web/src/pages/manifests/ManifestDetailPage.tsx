import type { JSX } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';

import { useDeleteManifest, useManifest } from '../../api/manifests';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { TabPanel, Tabs } from '../../components/Tabs';
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

      <Tabs
        label="Manifest sections"
        tabs={[
          { id: 'overview', label: TAB_LABEL.overview },
          { id: 'status', label: TAB_LABEL.status },
          { id: 'teams', label: TAB_LABEL.teams },
          { id: 'cargo', label: TAB_LABEL.cargo },
          { id: 'weight', label: TAB_LABEL.weight },
        ]}
        active={tab}
        onChange={selectTab}
      />

      {tab === 'overview' ? (
        <TabPanel id="overview">
          <DetailCard title="Manifest details">
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
          </DetailCard>
          <Gate policy="manifests:write">
            <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
              {!manifest.frozen ? (
                <LinkButton
                  to={`/manifests/${encodeURIComponent(manifest.id)}/edit`}
                  variant="secondary"
                >
                  Edit
                </LinkButton>
              ) : null}
              <Button
                variant="danger"
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
              </Button>
            </span>
          </Gate>
          {deleteError ? (
            <p role="alert" className="field__error">
              {deleteError}
            </p>
          ) : null}
        </TabPanel>
      ) : null}

      {tab === 'status' ? (
        <TabPanel id="status">
          <ManifestStatePanel manifest={manifest} />
        </TabPanel>
      ) : null}
      {tab === 'teams' ? (
        <TabPanel id="teams">
          <ManifestTeamsPanel manifestId={manifest.id} frozen={manifest.frozen} />
        </TabPanel>
      ) : null}
      {tab === 'cargo' ? (
        <TabPanel id="cargo">
          <ManifestBoxesPanel manifestId={manifest.id} frozen={manifest.frozen} />
        </TabPanel>
      ) : null}
      {tab === 'weight' ? (
        <TabPanel id="weight">
          <ManifestWeightPanel manifestId={manifest.id} />
        </TabPanel>
      ) : null}
    </section>
  );
}
