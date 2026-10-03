import { expect, test } from 'vitest';

import { makeReceiver } from '../../test/factories/receiver';
import { receiverOptions } from './useRegisteredReceivers';

test('offers registered receivers by organisation and region only', () => {
  const options = receiverOptions(
    [makeReceiver({ ref: 'a', organisation: 'Kyiv Aid', region: 'Kyiv Oblast' })],
    '',
  );

  expect(options).toEqual([{ value: 'a', label: 'Kyiv Aid — Kyiv Oblast' }]);
});

test('leaves out a receiver that is not registered', () => {
  const options = receiverOptions(
    [
      makeReceiver({ ref: 'p', status: 'Pending' }),
      makeReceiver({ ref: 's', status: 'Suspended' }),
      makeReceiver({ ref: 'e', status: 'Expired' }),
    ],
    '',
  );

  expect(options).toEqual([]);
});

test('keeps the receiver already chosen even when it no longer qualifies, saying why', () => {
  const options = receiverOptions(
    [
      makeReceiver({
        ref: 's',
        organisation: 'Odesa Shelter',
        region: 'Odesa',
        status: 'Suspended',
      }),
    ],
    's',
  );

  expect(options).toEqual([{ value: 's', label: 'Odesa Shelter — Odesa (Suspended)' }]);
});
