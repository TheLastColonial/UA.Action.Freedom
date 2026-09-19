import type { JSX } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';

import { useArriveConvoy, useConvoy, usePublishTruckList } from '../../api/convoys';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Button, LinkButton } from '../../components/Button';
import { DetailCard } from '../../components/DetailCard';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
import { TabPanel, Tabs } from '../../components/Tabs';
import { ConvoyDriversPanel } from './ConvoyDriversPanel';
import { ConvoyReadinessPanel } from './ConvoyReadinessPanel';
import { ConvoyVehiclesPanel } from './ConvoyVehiclesPanel';
import { RouteEditor } from './RouteEditor';

type Tab = 'overview' | 'route' | 'vehicles' | 'drivers';
const TABS: readonly Tab[] = ['overview', 'route', 'vehicles', 'drivers'];

export function ConvoyDetailPage(): JSX.Element {
  const { id = '' } = useParams();
  const convoyId = Number(id);
  const [searchParams, setSearchParams] = useSearchParams();
  const rawTab = searchParams.get('tab');
  const tab: Tab = TABS.includes(rawTab as Tab) ? (rawTab as Tab) : 'overview';

  const query = useConvoy(convoyId);
  const publish = usePublishTruckList(convoyId);
  const arrive = useArriveConvoy(convoyId);

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
  const problemOf = (error: unknown) =>
    error instanceof ApiDomainProblem ? (error.detail ?? error.message) : undefined;
  const actionError = problemOf(publish.error) ?? problemOf(arrive.error);

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
        <span>
          {convoy.arrived ? 'Arrived' : published ? 'Truck list published' : 'Truck list open'}
        </span>
      </header>

      <Tabs
        label="Convoy sections"
        tabs={[
          { id: 'overview', label: 'Overview' },
          { id: 'route', label: 'Route' },
          { id: 'vehicles', label: 'Vehicles' },
          { id: 'drivers', label: 'Crew' },
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
              <dt>Arrival</dt>
              <dd>
                {convoy.arrivedAt
                  ? `Arrived ${convoy.arrivedAt.slice(0, 16).replace('T', ' ')}`
                  : 'Not yet'}
              </dd>
            </dl>
          </DetailCard>

          <ConvoyReadinessPanel convoyId={convoy.id} />

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
              {published && !convoy.arrived ? (
                <Button
                  type="button"
                  variant="secondary"
                  disabled={arrive.isPending}
                  onClick={() => {
                    arrive.mutate();
                  }}
                >
                  Mark arrived
                </Button>
              ) : null}
            </span>
          </Gate>
          {actionError ? (
            <p role="alert" className="field__error">
              {actionError}
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
      {tab === 'drivers' ? (
        <TabPanel id="drivers">
          <ConvoyDriversPanel convoyId={convoy.id} />
        </TabPanel>
      ) : null}
    </section>
  );
}
