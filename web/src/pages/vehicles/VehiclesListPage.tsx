import type { JSX } from 'react';
import { Link, useSearchParams } from 'react-router-dom';

import { useVehicles } from '../../api/vehicles';
import type { VehicleReadModel } from '../../api/schemas/vehicles';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { Pagination } from '../../components/Pagination';
import { PageSkeleton } from '../../components/PageSkeleton';

const PAGE_SIZE = 50;

const columns: readonly Column<VehicleReadModel>[] = [
  {
    header: 'VIN',
    cell: (v) => <Link to={`/vehicles/${encodeURIComponent(v.vin)}`}>{v.vin}</Link>,
  },
  { header: 'Plate', cell: (v) => v.plate },
  { header: 'Make / model', cell: (v) => [v.brand, v.model].filter(Boolean).join(' ') || '—' },
  { header: 'Year', cell: (v) => v.year },
  { header: 'Fuel', cell: (v) => v.fuel },
  { header: 'Weight (kg)', cell: (v) => v.weightKg },
  { header: 'Servicing', cell: (v) => (v.servicing ? 'Yes' : 'No') },
];

export function VehiclesListPage(): JSX.Element {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number(searchParams.get('page') ?? '1'));
  const query = useVehicles({ page, pageSize: PAGE_SIZE });

  const setPage = (next: number) => {
    setSearchParams((params) => {
      params.set('page', String(next));
      return params;
    });
  };

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Vehicles</h1>
        <Gate policy="vehicles:write">
          <Link to="/vehicles/new">New vehicle</Link>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}

      {query.isError ? (
        <p role="alert">The vehicle list could not be loaded. {query.error.message}</p>
      ) : null}

      {query.isSuccess ? (
        <>
          <DataTable
            caption="Donated vehicles"
            columns={columns}
            rows={query.data}
            rowKey={(v) => v.vin}
            emptyMessage="No vehicles recorded yet."
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
