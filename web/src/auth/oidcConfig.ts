import { InMemoryWebStorage, WebStorageStateStore } from 'oidc-client-ts';
import type { AuthProviderProps } from 'react-oidc-context';

import { env } from '../env';

const appOrigin = `${window.location.origin}/app/`;

// Authorization Code + PKCE against the public `freedom-spa` client. The access token is
// held in memory only — a reload does a silent (prompt=none) renew against the Keycloak SSO
// cookie. Only the transient auth-code state touches sessionStorage.
export const oidcConfig: AuthProviderProps = {
  authority: env.VITE_OIDC_AUTHORITY,
  client_id: env.VITE_OIDC_CLIENT_ID,
  redirect_uri: appOrigin,
  post_logout_redirect_uri: appOrigin,
  response_type: 'code',
  scope: 'openid profile',
  automaticSilentRenew: true,
  accessTokenExpiringNotificationTimeInSeconds: 120,
  userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
  stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
  onSigninCallback: () => {
    window.history.replaceState({}, document.title, appOrigin);
  },
};
