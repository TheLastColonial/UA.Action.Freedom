import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useBoxes } from '../../api/boxes';
import type { BoxReadModel } from '../../api/schemas/boxes';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';
import { Pagination } from '../../components/Pagination';

const PAGE_SIZE = 50;

const columns: readonly Column<BoxReadModel>[] = [
  { header: 'Box', cell: (b) => <Link to={`/boxes/${String(b.id)}`}>#{b.id}</Link> },
  { header: 'Weight (kg)', cell: (b) => b.weightKg },
  { header: 'Receiver', cell: (b) => b.receiverRef ?? '—' },
  { header: 'State', cell: (b) => (b.validated ? 'Validated' : 'Open') },
];

export function BoxesListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));
  const query = useBoxes({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Boxes</h1>
        <Gate policy="boxes:write">
          <Link to="/boxes/new">New box</Link>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The box list could not be loaded.</p> : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Packed boxes"
            columns={columns}
            rows={query.data}
            rowKey={(b) => String(b.id)}
            emptyMessage="No boxes packed yet."
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
