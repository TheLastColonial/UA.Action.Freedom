import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useManifests } from '../../api/manifests';
import type { ManifestReadModel } from '../../api/schemas/manifests';
import { LinkButton } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<ManifestReadModel>[] = [
  {
    header: 'Reference',
    cell: (m) => <Link to={`/manifests/${encodeURIComponent(m.id)}`}>{m.id}</Link>,
  },
  { header: 'Status', cell: (m) => m.status },
  { header: 'Vehicle', cell: (m) => m.vin },
  { header: 'Convoy', cell: (m) => m.convoyId },
  { header: 'Frozen', cell: (m) => (m.frozen ? 'Yes' : 'No') },
];

export function ManifestsListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));
  const query = useManifests({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Manifests</h1>
        {/* A manifest is opened on its convoy's truck list, against the vehicle it describes. */}
        <Gate policy="convoys:read">
          <LinkButton to="/convoys">Open one from a convoy</LinkButton>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The manifest list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Manifests"
            columns={columns}
            rows={query.data}
            rowKey={(m) => m.id}
            emptyMessage="No manifests yet."
          />
          <Pagination
            page={page}
            hasNext={query.data.length === PAGE_SIZE}
            onPageChange={setPage}
          />
        </>
      ) : null}
    </section>
  );
}
