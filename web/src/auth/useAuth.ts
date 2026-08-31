import { useContext } from 'react';

import { AuthContext } from './AuthContext';
import type { FreedomAuth } from './AuthContext';

export function useAuth(): FreedomAuth {
  const auth = useContext(AuthContext);
  if (!auth) {
    throw new Error('useAuth must be used inside <FreedomAuthProvider>');
  }
  return auth;
}
