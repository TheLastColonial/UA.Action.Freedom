import type { ManifestStatus } from '../../api/schemas/common';

export type ManifestVerb =
  'propose' | 'reject' | 'approve' | 'prepare' | 'ready' | 'depart' | 'deliver' | 'lose' | 'return';

interface Edge {
  verb: ManifestVerb;
  from: readonly ManifestStatus[];
  to: ManifestStatus;
}

// Mirrors ManifestTransitions.Allowed in src/UA.Action.Freedom.Domain/Manifest.cs. The API
// is the authority; this only shapes which buttons the panel offers.
const EDGES: readonly Edge[] = [
  { verb: 'propose', from: ['Created', 'Rejected'], to: 'Proposed' },
  { verb: 'reject', from: ['Created', 'Proposed'], to: 'Rejected' },
  { verb: 'approve', from: ['Proposed'], to: 'Confirmed' },
  { verb: 'prepare', from: ['Confirmed'], to: 'Preparing' },
  { verb: 'ready', from: ['Preparing'], to: 'Ready' },
  { verb: 'depart', from: ['Ready'], to: 'InTransit' },
  { verb: 'deliver', from: ['InTransit'], to: 'Delivered' },
  { verb: 'lose', from: ['InTransit'], to: 'Lost' },
  { verb: 'return', from: ['Delivered'], to: 'Returned' },
];

const VERB_LABEL: Record<ManifestVerb, string> = {
  propose: 'Propose',
  reject: 'Reject',
  approve: 'Approve',
  prepare: 'Start preparing',
  ready: 'Mark ready',
  depart: 'Depart',
  deliver: 'Mark delivered',
  lose: 'Mark lost',
  return: 'Mark returned',
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
  frozen: boolean;
  canApprove: boolean;
  hasConvoy: boolean;
  convoyTruckListPublished: boolean;
}

export function availableTransitions(ctx: TransitionContext): TransitionOption[] {
  return EDGES.filter((edge) => edge.from.includes(ctx.status))
    .filter((edge) => {
      // A frozen manifest still reports what happened to the load, but cannot be reopened.
      if (ctx.frozen && (edge.verb === 'propose' || edge.verb === 'reject')) {
        return false;
      }
      // Approval releases the GMR and freezes the manifest — Administrator alone.
      if (edge.verb === 'approve' && !ctx.canApprove) {
        return false;
      }
      return true;
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
  if (!ctx.hasConvoy) {
    return 'Link this manifest to a convoy before proposing it.';
  }
  if (!ctx.convoyTruckListPublished) {
    return "The convoy's truck list has not been published yet.";
  }
  return null;
}
