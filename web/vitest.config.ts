import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  // Browser Mode pre-bundles test deps separately; without deduping, libraries that do
  // `import React from 'react'` (react-query, react-router) can receive a second, empty copy.
  resolve: {
    dedupe: ['react', 'react-dom'],
  },
  optimizeDeps: {
    include: [
      'react',
      'react-dom',
      'react-dom/client',
      'react/jsx-dev-runtime',
      '@tanstack/react-query',
      'react-router-dom',
      'react-oidc-context',
      'oidc-client-ts',
    ],
  },
  test: {
    css: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    browser: {
      enabled: true,
      provider: 'playwright',
      headless: true,
      instances: [{ browser: 'chromium' }],
    },
  },
});
