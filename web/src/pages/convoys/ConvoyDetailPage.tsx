import type { JSX } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';

import { useConvoy, usePublishTruckList } from '../../api/convoys';
import { ApiDomainProblem, ApiNotFound } from '../../api/problem';
import { Gate } from '../../components/Gate';
import { NotFound } from '../../components/NotFound';
import { PageSkeleton } from '../../components/PageSkeleton';
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

      <nav aria-label="Convoy sections" style={{ display: 'flex', gap: 'var(--space-3)' }}>
        {TABS.map((name) => (
          <button
            key={name}
            type="button"
            aria-current={tab === name ? 'page' : undefined}
            onClick={() => {
              selectTab(name);
            }}
          >
            {name[0]?.toUpperCase()}
            {name.slice(1)}
          </button>
        ))}
      </nav>

      {tab === 'overview' ? (
        <div>
          <dl>
            <dt>Departs</dt>
            <dd>{convoy.start.slice(0, 16).replace('T', ' ')}</dd>
            <dt>Expected arrival</dt>
            <dd>{convoy.expectedEnd.slice(0, 16).replace('T', ' ')}</dd>
            <dt>Truck list</dt>
            <dd>{published ? `Published ${convoy.truckListPublishedAt ?? ''}` : 'Open'}</dd>
          </dl>

          <Gate policy="convoys:write">
            <span style={{ display: 'flex', gap: 'var(--space-3)' }}>
              <Link to={`/convoys/${String(convoy.id)}/edit`}>Edit</Link>
              {!published ? (
                <button
                  type="button"
                  disabled={publish.isPending}
                  onClick={() => {
                    publish.mutate();
                  }}
                >
                  Publish truck list
                </button>
              ) : null}
            </span>
          </Gate>
          {publishError ? (
            <p role="alert" className="field__error">
              {publishError}
            </p>
          ) : null}
        </div>
      ) : null}

      {tab === 'route' ? <RouteEditor convoyId={convoy.id} disabled={published} /> : null}
      {tab === 'vehicles' ? (
        <ConvoyVehiclesPanel convoyId={convoy.id} disabled={published} />
      ) : null}
    </section>
  );
}
