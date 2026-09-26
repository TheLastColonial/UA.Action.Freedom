import { defineConfig } from 'vite';
import type { Connect, Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// The API has no CORS configuration and is only reachable same-origin behind the edge.
// In dev the SPA runs on :5173 and every API path is proxied to the running API so the
// browser stays same-origin. Override the target with VITE_API_PROXY_TARGET when the API
// is somewhere other than the local edge (e.g. `dotnet run` on :5100).
const apiProxyTarget = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8080';

const apiPrefixes = [
  '/vehicles',
  '/people',
  '/convoys',
  '/receivers',
  '/boxes',
  '/manifests',
  '/health',
];

// "/app" without the slash is what people type or paste; Vite only serves the base with it and
// shows a "did you mean /app/?" page otherwise. The API host redirects the same way.
const redirectBareBase: Connect.NextHandleFunction = (req, res, next) => {
  const url = req.url ?? '';
  if (url === '/app' || url.startsWith('/app?')) {
    res.statusCode = 302;
    res.setHeader('Location', `/app/${url.slice('/app'.length)}`);
    res.end();
    return;
  }
  next();
};

const appBaseRedirect: Plugin = {
  name: 'freedom-app-base-redirect',
  configureServer(server) {
    server.middlewares.use(redirectBareBase);
  },
  configurePreviewServer(server) {
    server.middlewares.use(redirectBareBase);
  },
};

export default defineConfig({
  // Served under /app by the API host so SPA routes never collide with an API route.
  base: '/app/',
  plugins: [react(), appBaseRedirect],
  server: {
    port: 5173,
    proxy: Object.fromEntries(
      apiPrefixes.map((prefix) => [prefix, { target: apiProxyTarget, changeOrigin: true }]),
    ),
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
