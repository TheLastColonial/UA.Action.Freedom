import { createContext } from 'react';

import type { Identity } from './identity';

export interface FreedomAuth extends Identity {
  readonly isLoading: boolean;
  readonly isAuthenticated: boolean;
  readonly signIn: () => void;
  readonly signOut: () => void;
  readonly getAccessToken: () => string | undefined;
}

export const AuthContext = createContext<FreedomAuth | null>(null);
