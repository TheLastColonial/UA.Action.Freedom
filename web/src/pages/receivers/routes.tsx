import type { RouteObject } from 'react-router-dom';

import { ReceiverCreatePage } from './ReceiverCreatePage';
import { ReceiverDetailPage } from './ReceiverDetailPage';
import { ReceiverEditPage } from './ReceiverEditPage';
import { ReceiversListPage } from './ReceiversListPage';

export const receiverRoutes: RouteObject[] = [
  { index: true, element: <ReceiversListPage /> },
  { path: 'new', element: <ReceiverCreatePage /> },
  { path: ':ref', element: <ReceiverDetailPage /> },
  { path: ':ref/edit', element: <ReceiverEditPage /> },
];
