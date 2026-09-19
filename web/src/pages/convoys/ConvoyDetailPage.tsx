import type { JSX } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';

import { useConvoy, usePublishTruckList } from '../../api/convoys';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { TabPanel, Tabs } from '../../components/Tabs';
import { ConvoyVehiclesPanel } from './ConvoyVehiclesPanel';
import { RouteEditor } from './RouteEditor';

type Tab = 'overview' | 'route' | 'vehicles';
const TABS: readonly Tab[] = ['overview', 'route', 'vehicles'];

export function ConvoyDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const convoyId = Number(id);
  const [searchParams, setSearchParams] = useSearchParams();
  const rawTab = searchParams.get('tab');
  const tab: Tab = TABS.includes(rawTab as Tab) ? (rawTab as Tab) : 'overview';

  const query = useConvoy(convoyId);
  const publish = usePublishTruckList(convoyId);

  if (query.isError && query.error instanceof ApiNotFound) {
    return <NotFound />;
  }
  if (query.isPending) {
    return <PageSkeleton />;
  }
  if (query.isError) {
    return <p role="alert">The convoy could not be loaded.</p>;
  }

  const convoy = query.data;
  const published = convoy.truckListPublished;
  const publishError =
    publish.error instanceof ApiDomainProblem
      ? (publish.error.detail ?? publish.error.message)
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
        <h1>Convoy #{convoy.id}</h1>
        <span>{published ? 'Truck list published' : 'Truck list open'}</span>
      </header>

      <Tabs
        label="Convoy sections"
        tabs={[
          { id: 'overview', label: 'Overview' },
          { id: 'route', label: 'Route' },
          { id: 'vehicles', label: 'Vehicles' },
        ]}
        active={tab}
        onChange={selectTab}
      />

      {tab === 'overview' ? (
        <TabPanel id="overview">
          <DetailCard title="Convoy details">
            <dl>
              <dt>Departs</dt>
              <dd>{convoy.start.slice(0, 16).replace('T', ' ')}</dd>
              <dt>Expected arrival</dt>
              <dd>{convoy.expectedEnd.slice(0, 16).replace('T', ' ')}</dd>
              <dt>Truck list</dt>
              <dd>{published ? `Published ${convoy.truckListPublishedAt ?? ''}` : 'Open'}</dd>
            </dl>
          </DetailCard>

          <Gate policy="convoys:write">
            <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
              <LinkButton to={`/convoys/${String(convoy.id)}/edit`} variant="secondary">
                Edit
              </LinkButton>
              {!published ? (
                <Button
                  type="button"
                  disabled={publish.isPending}
                  onClick={() => {
                    publish.mutate();
                  }}
                >
                  Publish truck list
                </Button>
              ) : null}
            </span>
          </Gate>
          {publishError ? (
            <p role="alert" className="field__error">
              {publishError}
            </p>
          ) : null}
        </TabPanel>
      ) : null}

      {tab === 'route' ? (
        <TabPanel id="route">
          <RouteEditor convoyId={convoy.id} disabled={published} />
        </TabPanel>
      ) : null}
      {tab === 'vehicles' ? (
        <TabPanel id="vehicles">
          <ConvoyVehiclesPanel convoyId={convoy.id} disabled={published} />
        </TabPanel>
      ) : null}
    </section>
  );
}
