import type { ReceiverDetailReadModel } from '../../api/receiverDetail';
import type { ReceiverReadModel } from '../../api/schemas/receivers';

let seq = 0;

export function makeReceiver(overrides: Partial<ReceiverReadModel> = {}): ReceiverReadModel {
  seq += 1;
  return {
    ref: `cccccccc-0000-0000-0000-${String(seq).padStart(12, '0')}`,
    organisation: `Aid Partner ${String(seq)}`,
    region: 'Kyiv Oblast',
    // Registered by default so a picker has something to offer; a test about the other states overrides it.
    status: 'Registered',
    lastChangedByName: null,
    lastChangedAt: null,
    ...overrides,
  };
}

export function makeReceiverDetail(
  overrides: Partial<ReceiverDetailReadModel> = {},
): ReceiverDetailReadModel {
  return {
    ref: 'cccccccc-0000-0000-0000-000000000001',
    contactName: 'Iryna Shevchenko',
    contactPhone: '+380 44 123 4567',
    addressLine1: '17 Khreshchatyk',
    addressLine2: null,
    city: 'Kyiv',
    postCode: '01001',
    deleteAfter: null,
    ...overrides,
  };
}
