import { createContext } from 'react';

import type { Identity } from './identity';

export interface FreedomAuth extends Identity {
  readonly isLoading: boolean;
  readonly isAuthenticated: boolean;
  /** `returnTo` is the in-app path to land on once signed in, so a reload keeps the page. */
  readonly signIn: (returnTo?: string) => void;
  readonly signOut: () => void;
  readonly getAccessToken: () => string | undefined;
}

export const AuthContext = createContext<FreedomAuth | null>(null);
