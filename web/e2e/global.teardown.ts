import { existsSync, readFileSync, rmSync } from 'node:fs';

import { test as teardown } from '@playwright/test';

import { MARK_FILE, cleanSince } from './db';
import { stackIsUp } from './stack';

// Runs once after the chromium project (including when specs fail — that is the point). Deletes
// every row created since db.setup.ts took its marker, which is the only way to clear an
// approved manifest: the API freezes it and DELETE returns 409. Scoped by time, so manual test
// data from before the run is left alone. For a full wipe use iac/local/reset-data.sh.
teardown('remove rows the e2e run created', async () => {
  if (!(await stackIsUp())) {
    teardown.skip(true, 'the local stack is not up (docker compose + tofu apply)');
    return;
  }

  if (!existsSync(MARK_FILE)) {
    return;
  }

  try {
    const mark = readFileSync(MARK_FILE, 'utf8').trim();
    if (mark.length > 0) {
      cleanSince(mark);
    }
  } catch (error) {
    console.warn('[e2e teardown] data cleanup skipped:', error);
  } finally {
    rmSync(MARK_FILE, { force: true });
  }
});
