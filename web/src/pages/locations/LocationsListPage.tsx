import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useLocations } from '../../api/locations';
import type { LocationReadModel } from '../../api/schemas/locations';
import { LinkButton } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<LocationReadModel>[] = [
  { header: 'Name', cell: (l) => <Link to={`/locations/${String(l.id)}`}>{l.name}</Link> },
  { header: 'City', cell: (l) => l.city ?? '—' },
  { header: 'Postcode', cell: (l) => l.postcode ?? '—' },
];

export function LocationsListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));

  const query = useLocations({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Distribution hubs</h1>
        <Gate policy="locations:write">
          <LinkButton to="/locations/new">New location</LinkButton>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The location list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Distribution hubs"
            columns={columns}
            rows={query.data}
            rowKey={(l) => String(l.id)}
            emptyMessage="No locations recorded yet."
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
