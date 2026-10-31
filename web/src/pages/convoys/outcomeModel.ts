import type { BoxOutcome } from '../../api/schemas/outcomes';

/** The outcomes a box may take next. Mirrors Domain/BoxOutcome.cs; the API is what enforces it. */
export function nextOutcomes(current: BoxOutcome | undefined): readonly BoxOutcome[] {
  if (current === undefined) {
    return ['Delivered', 'Seized', 'ReturnedToHub', 'Undeliverable'];
  }
  return current === 'Delivered' ? ['Accepted', 'Seized', 'ReturnedToHub', 'Undeliverable'] : [];
}
