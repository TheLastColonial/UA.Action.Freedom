import { describe, expect, it } from 'vitest';

import type { ManifestStatus } from '../../api/schemas/common';
import { availableTransitions } from './transitions';
import type { ManifestVerb } from './transitions';

const ctx = (over: Partial<Parameters<typeof availableTransitions>[0]> = {}) => ({
  status: 'Proposed' as ManifestStatus,
  canApprove: true,
  convoyTruckListPublished: true,
  ...over,
});

const verbs = (status: ManifestStatus, over = {}): ManifestVerb[] =>
  availableTransitions(ctx({ status, ...over })).map((t) => t.verb);

describe('availableTransitions — the edge table', () => {
  const expected: Record<ManifestStatus, ManifestVerb[]> = {
    Proposed: ['reject', 'approve'],
    Rejected: ['propose'],
    Approved: [],
  };

  for (const [status, want] of Object.entries(expected) as [ManifestStatus, ManifestVerb[]][]) {
    it(`from ${status} offers exactly ${want.join(', ') || '(nothing)'}`, () => {
      expect(verbs(status).sort()).toEqual([...want].sort());
    });
  }
});

describe('extra rules', () => {
  it('hides approve from someone who cannot approve', () => {
    expect(verbs('Proposed', { canApprove: false })).toEqual(['reject']);
  });

  it('offers nothing from Approved, because only a load change sends it back', () => {
    expect(verbs('Approved')).toEqual([]);
  });

  it('disables propose with a reason when the truck list is not published', () => {
    const [propose] = availableTransitions(
      ctx({ status: 'Rejected', convoyTruckListPublished: false }),
    );
    expect(propose?.disabledReason).toBe("The convoy's truck list has not been published yet.");
  });

  it('leaves propose enabled once the convoy is linked and published', () => {
    const [propose] = availableTransitions(ctx({ status: 'Rejected' }));
    expect(propose?.disabledReason).toBeNull();
  });
});
