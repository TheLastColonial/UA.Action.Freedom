import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useConvoys } from '../../api/convoys';
import type { ConvoyReadModel } from '../../api/schemas/convoys';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<ConvoyReadModel>[] = [
  {
    header: 'Convoy',
    cell: (c) => <Link to={`/convoys/${String(c.id)}`}>#{c.id}</Link>,
  },
  { header: 'Departs', cell: (c) => c.start.slice(0, 16).replace('T', ' ') },
  { header: 'Expected arrival', cell: (c) => c.expectedEnd.slice(0, 16).replace('T', ' ') },
  {
    header: 'Truck list',
    cell: (c) => (c.truckListPublished ? 'Published' : 'Open'),
  },
];

export function ConvoysListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));
  const query = useConvoys({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Convoys</h1>
        <Gate policy="convoys:write">
          <Link to="/convoys/new">New convoy</Link>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The convoy list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Convoys"
            columns={columns}
            rows={query.data}
            rowKey={(c) => String(c.id)}
            emptyMessage="No convoys planned yet."
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
