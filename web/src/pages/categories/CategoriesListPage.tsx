import type { JSX } from 'react';
import { Link } from 'react-router-dom';

import { useCategories } from '../../api/categories';
import type { ItemCategoryReadModel } from '../../api/schemas/categories';
import { LinkButton } from '../../components/Button';
import { DataTable } from '../../components/DataTable';
import type { Column } from '../../components/DataTable';
import { Gate } from '../../components/Gate';
import { PageSkeleton } from '../../components/PageSkeleton';

function flagsOf(category: ItemCategoryReadModel): string {
  const flags: string[] = [];
  if (category.hazardClass !== null) flags.push(`Hazard class ${String(category.hazardClass)}`);
  if (category.isSensitive) flags.push('Sensitive');
  if (category.isNotCarried) flags.push('Not carried');
  return flags.length > 0 ? flags.join(', ') : '—';
}

const columns: readonly Column<ItemCategoryReadModel>[] = [
  {
    header: 'Name',
    cell: (c) => (c.isFixed ? `${c.nameEn} (built in)` : c.nameEn),
  },
  { header: 'Ukrainian', cell: (c) => (c.nameUk.length > 0 ? c.nameUk : '—') },
  { header: 'Handling', cell: flagsOf },
  {
    header: 'Short shelf life',
    cell: (c) => (c.warnWithinDays === null ? '—' : `${String(c.warnWithinDays)} days`),
  },
  { header: 'UK code', cell: (c) => c.ukCode ?? '—' },
  { header: 'EU code', cell: (c) => c.euCode ?? '—' },
  { header: 'UA code', cell: (c) => c.uaCode ?? '—' },
  {
    header: 'Actions',
    cell: (c) => (
      <Gate policy="categories:write">
        <Link to={`/categories/${String(c.id)}/edit`} aria-label={`Edit ${c.nameEn}`}>
          Edit
        </Link>
      </Gate>
    ),
  },
];

export function CategoriesListPage(): JSX.Element {
  const query = useCategories();

  return (
    <section>
      <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
        <h1>Item categories</h1>
        <Gate policy="categories:write">
          <LinkButton to="/categories/new">New category</LinkButton>
        </Gate>
      </header>

      {query.isPending ? <PageSkeleton /> : null}
      {query.isError ? <p role="alert">The categories could not be loaded.</p> : null}

      {query.isSuccess ? (
        <DataTable
          caption="Item categories"
          columns={columns}
          rows={query.data}
          rowKey={(c) => String(c.id)}
          emptyMessage="No categories recorded yet."
        />
      ) : null}
    </section>
  );
}
