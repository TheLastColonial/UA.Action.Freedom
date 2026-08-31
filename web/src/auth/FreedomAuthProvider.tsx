import type { JSX, ReactNode } from 'react';
import { useEffect, useMemo } from 'react';
import { AuthProvider as OidcAuthProvider, useAuth as useOidcAuth } from 'react-oidc-context';

import { configureApiClient } from '../api/client';
import { emitSlowRequest } from '../api/slowRequestSignal';
import { AuthContext } from './AuthContext';
import type { FreedomAuth } from './AuthContext';
import { deriveIdentity } from './identity';
import { oidcConfig } from './oidcConfig';

export function FreedomAuthProvider({ children }: { children: ReactNode }): JSX.Element {
  return (
    <OidcAuthProvider {...oidcConfig}>
      <FreedomAuthBridge>{children}</FreedomAuthBridge>
    </OidcAuthProvider>
  );
}

function FreedomAuthBridge({ children }: { children: ReactNode }): JSX.Element {
  const oidc = useOidcAuth();
  const accessToken = oidc.user?.access_token;

  const value = useMemo<FreedomAuth>(() => {
    const identity = deriveIdentity(oidc.user?.profile);
    return {
      ...identity,
      isLoading: oidc.isLoading,
      isAuthenticated: oidc.isAuthenticated,
      signIn: () => {
        void oidc.signinRedirect();
      },
      signOut: () => {
        void oidc.signoutRedirect();
      },
      getAccessToken: () => oidc.user?.access_token,
    };
  }, [oidc]);

  useEffect(() => {
    configureApiClient({
      getAccessToken: () => oidc.user?.access_token,
      onUnauthorized: async () => {
        try {
          await oidc.signinSilent();
        } catch {
          void oidc.signinRedirect();
        }
      },
      onSlowRequest: emitSlowRequest,
    });
    // oidc identity is captured through the token; re-bind whenever it rotates.
  }, [oidc, accessToken]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
