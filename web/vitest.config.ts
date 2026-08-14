import { defineConfig } from 'vitest/config';

// The runner deliberately does not reuse vite.config.ts. That config builds the shipped bundle and
// carries plugins the tests do not need; sharing it would make a build change able to break the
// tests for reasons unrelated to either.
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
    // The repo lives on an NTFS mount where npm cannot create .bin symlinks, so the runner is
    // invoked through node rather than a bin shim. See the test script in package.json.
    globals: false,
  },
});
