import type { JSX } from 'react';

import { Button } from './Button';

interface PaginationProps {
  page: number;
  hasNext: boolean;
  onPageChange: (page: number) => void;
}

/**
 * The API returns a bare array with no total count, so "next" is offered only while the
 * current page came back full.
 */
export function Pagination({ page, hasNext, onPageChange }: PaginationProps): JSX.Element {
  return (
    <nav
      aria-label="Pagination"
      style={{ display: 'flex', gap: 'var(--space-3)', alignItems: 'center' }}
    >
      <Button
        variant="secondary"
        disabled={page <= 1}
        onClick={() => {
          onPageChange(page - 1);
        }}
      >
        Previous
      </Button>
      <span aria-current="page">Page {page}</span>
      <Button
        variant="secondary"
        disabled={!hasNext}
        onClick={() => {
          onPageChange(page + 1);
        }}
      >
        Next
      </Button>
    </nav>
  );
}
