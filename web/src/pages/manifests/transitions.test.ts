import { describe, expect, it } from 'vitest';

import type { ManifestStatus } from '../../api/schemas/common';
import { availableTransitions } from './transitions';
import type { ManifestVerb } from './transitions';

const ctx = (over: Partial<Parameters<typeof availableTransitions>[0]> = {}) => ({
  status: 'Created' as ManifestStatus,
  frozen: false,
  canApprove: true,
  hasConvoy: true,
  convoyTruckListPublished: true,
  ...over,
});

const verbs = (status: ManifestStatus, over = {}): ManifestVerb[] =>
  availableTransitions(ctx({ status, ...over })).map((t) => t.verb);

describe('availableTransitions — the edge table', () => {
  const expected: Record<ManifestStatus, ManifestVerb[]> = {
    Created: ['propose', 'reject'],
    Proposed: ['reject', 'approve'],
    Rejected: ['propose'],
    Confirmed: ['prepare'],
    Preparing: ['ready'],
    Ready: ['depart'],
    InTransit: ['deliver', 'lose'],
    Delivered: ['return'],
    Lost: [],
    Returned: [],
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

  it('when frozen, drops propose and reject but keeps forward progress', () => {
    expect(verbs('Rejected', { frozen: true })).toEqual([]);
    expect(verbs('Preparing', { frozen: true })).toEqual(['ready']);
    expect(verbs('InTransit', { frozen: true }).sort()).toEqual(['deliver', 'lose']);
  });

  it('disables propose with a reason when there is no convoy', () => {
    const [propose] = availableTransitions(ctx({ status: 'Created', hasConvoy: false }));
    expect(propose?.verb).toBe('propose');
    expect(propose?.disabledReason).toBe('Link this manifest to a convoy before proposing it.');
  });

  it('disables propose with a reason when the truck list is not published', () => {
    const [propose] = availableTransitions(
      ctx({ status: 'Created', convoyTruckListPublished: false }),
    );
    expect(propose?.disabledReason).toBe("The convoy's truck list has not been published yet.");
  });

  it('leaves propose enabled once the convoy is linked and published', () => {
    const [propose] = availableTransitions(ctx({ status: 'Created' }));
    expect(propose?.disabledReason).toBeNull();
  });
});
