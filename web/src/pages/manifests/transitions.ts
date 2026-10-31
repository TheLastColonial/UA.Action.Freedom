import type { ManifestStatus } from '../../api/schemas/common';

export type ManifestVerb = 'propose' | 'reject' | 'approve';

interface Edge {
  verb: ManifestVerb;
  from: readonly ManifestStatus[];
  to: ManifestStatus;
}

// Mirrors ManifestTransitions.CanBeRequested in src/UA.Action.Freedom.Domain/Manifest.cs. The API
// is the authority; this only shapes which buttons the panel offers. Approved back to Proposed is
// not here: only a change to the vehicle's load takes that edge.
const EDGES: readonly Edge[] = [
  { verb: 'propose', from: ['Rejected'], to: 'Proposed' },
  { verb: 'reject', from: ['Proposed'], to: 'Rejected' },
  { verb: 'approve', from: ['Proposed'], to: 'Approved' },
];

const VERB_LABEL: Record<ManifestVerb, string> = {
  propose: 'Propose',
  reject: 'Reject',
  approve: 'Approve',
};

export interface TransitionOption {
  verb: ManifestVerb;
  to: ManifestStatus;
  label: string;
  /** Non-null when the button should render disabled, with this explanation. */
  disabledReason: string | null;
}

export interface TransitionContext {
  status: ManifestStatus;
  canApprove: boolean;
  // There is no `hasConvoy`: a manifest is opened against a truck-list entry, so it always names
  // one. What is still worth asking is whether that list has been closed.
  convoyTruckListPublished: boolean;
}

export function availableTransitions(ctx: TransitionContext): TransitionOption[] {
  return EDGES.filter((edge) => edge.from.includes(ctx.status))
    .filter((edge) => {
      // Approval signs off the load — Administrator alone. It files nothing.
      return edge.verb !== 'approve' || ctx.canApprove;
    })
    .map((edge) => ({
      verb: edge.verb,
      to: edge.to,
      label: VERB_LABEL[edge.verb],
      disabledReason: proposeBlockReason(edge.verb, ctx),
    }));
}

function proposeBlockReason(verb: ManifestVerb, ctx: TransitionContext): string | null {
  if (verb !== 'propose') {
    return null;
  }
  if (!ctx.convoyTruckListPublished) {
    return "The convoy's truck list has not been published yet.";
  }
  return null;
}
