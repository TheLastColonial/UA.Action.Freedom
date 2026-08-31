import type { JSX, ReactNode } from 'react';

import './DataTable.css';

export interface Column<T> {
  header: string;
  cell: (row: T) => ReactNode;
}

interface DataTableProps<T> {
  caption: string;
  columns: readonly Column<T>[];
  rows: readonly T[];
  rowKey: (row: T) => string;
  emptyMessage: string;
}

export function DataTable<T>({
  caption,
  columns,
  rows,
  rowKey,
  emptyMessage,
}: DataTableProps<T>): JSX.Element {
  if (rows.length === 0) {
    return <p className="data-table__empty">{emptyMessage}</p>;
  }

  return (
    <div className="data-table__scroll">
      <table className="data-table">
        <caption className="data-table__caption">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column.header} scope="col">
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column) => (
                <td key={column.header}>{column.cell(row)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
