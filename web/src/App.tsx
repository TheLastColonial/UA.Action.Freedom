import type { JSX } from 'react';
import { RouterProvider, createBrowserRouter } from 'react-router-dom';

import { routes } from './routes';

const router = createBrowserRouter(routes, { basename: '/app' });

export function App(): JSX.Element {
  return <RouterProvider router={router} />;
}
